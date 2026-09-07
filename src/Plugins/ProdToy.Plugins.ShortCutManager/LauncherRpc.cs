using System.Text.Json;
using System.Text.Json.Nodes;
using ProdToy.Sdk;

namespace ProdToy.Plugins.ShortCutManager;

/// <summary>
/// Handlers for the <c>shortcuts.launcher.*</c> RPC pipe commands — the bridge
/// that lets a Claude CLI session (via <c>ProdToy.exe launcher …</c> or the
/// <c>--mcp</c> server) drive the Consolidated Launcher: stop-all, launch-all,
/// restart-all, status, folders. All handlers run on the UI thread (RpcRouter
/// contract) and return single-line JSON with at least {"ok":bool,"message":…}.
/// </summary>
static class LauncherRpc
{
    /// <summary>Verbs that need a launcher window (opening it if necessary).</summary>
    public static async Task<string> HandleAsync(IPluginContext ctx, string verb, PipeCommand cmd)
    {
        try
        {
            string? requested = ParseFolder(cmd.PayloadJson);

            if (verb == "folders")
                return FoldersJson();

            // Row-level verbs resolve the shortcut first — a unique shortcut
            // name works without a folder argument (its folder wins).
            if (verb is "launch-one" or "stop-one" or "restart-one" or "logs")
                return await HandleRowVerbAsync(ctx, verb, cmd, requested);

            var resolved = ResolveFolder(requested);
            if (resolved.Error != null) return Fail(resolved.Error);
            string folder = resolved.Folder!;
            string display = folder.Length == 0 ? "(root)" : folder;

            // status never force-opens a window — report closed-state instead.
            if (verb == "status")
            {
                var open = ConsolidatedLauncherForm.TryGetOpen(folder);
                if (open != null) return open.RpcStatusJson();
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    folder = display,
                    launcherOpen = false,
                    message = "Consolidated Launcher window is not open for this folder — no live process state. "
                        + "launch-all / restart-all / stop-all will open it.",
                    shortcuts = ShortcutsIn(folder).Select(s => new { id = s.Id, name = s.Name }),
                });
            }

            bool wasOpen = ConsolidatedLauncherForm.TryGetOpen(folder) != null;
            var form = ConsolidatedLauncherForm.GetOrCreate(
                ctx.Host.CurrentTheme, folder, ShortcutsIn(folder));

            // A freshly opened window hasn't run its first process probe yet, so
            // externally started processes aren't matched. Give the probe a
            // cycle before stopping, or Stop All would miss them.
            if (!wasOpen && verb is "stop-all" or "restart-all")
                await Task.Delay(4000);
            if (form.IsDisposed) return Fail("Launcher window was closed while the command ran.");

            switch (verb)
            {
                case "launch-all":
                    form.RpcLaunchAll();
                    return Ok($"Launch All started for '{display}'. Launching is asynchronous — poll "
                        + "'launcher status' to see when apps are up.");

                case "stop-all":
                {
                    var r = await form.RpcStopAllAsync();
                    string msg = $"Stopped {r.Stopped} process(es) in '{display}'."
                        + (r.Kept > 0 ? $" {r.Kept} kept running (Keep running on Stop All)." : "")
                        + (r.AllExited ? "" : " Warning: some processes were still exiting after 15s.");
                    return Ok(msg);
                }

                case "restart-all":
                {
                    var r = await form.RpcRestartAllAsync();
                    string msg = $"Stopped {r.Stopped} process(es) in '{display}'"
                        + (r.Kept > 0 ? $" ({r.Kept} kept running)" : "")
                        + (r.AllExited ? "" : ", some still exiting after 15s")
                        + ". Launch All started — poll 'launcher status' to see when apps are up.";
                    return Ok(msg);
                }

                default:
                    return Fail($"Unknown launcher verb '{verb}'.");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"launcher RPC '{verb}' failed", ex);
            return Fail(ex.Message);
        }
    }

    // ─────────────────────────── row-level verbs ───────────────────────────

    private static async Task<string> HandleRowVerbAsync(
        IPluginContext ctx, string verb, PipeCommand cmd, string? requestedFolder)
    {
        string? name = ParseString(cmd.PayloadJson, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Missing 'name' — the shortcut's name (or id).");

        var rs = ResolveShortcut(requestedFolder, name!);
        if (rs.Error != null) return Fail(rs.Error);
        var s = rs.Shortcut!;
        string folder = ShortcutFolders.Normalize(s.FolderPath);
        string display = folder.Length == 0 ? "(root)" : folder;

        if (verb == "logs")
        {
            var open = ConsolidatedLauncherForm.TryGetOpen(folder);
            if (open == null)
                return Fail($"Launcher window for '{display}' is not open — session logs exist only "
                    + "while it is. Launch the shortcut (launcher_launch_one) first.");
            int lines = Math.Clamp(ParseInt(cmd.PayloadJson, "lines") ?? 100, 1, 2000);
            var log = open.RpcReadLog(s.Id, lines);
            if (log == null)
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    name = s.Name,
                    lines = Array.Empty<string>(),
                    message = "No console output for this shortcut in the current launcher session.",
                });
            return JsonSerializer.Serialize(new { ok = true, name = s.Name, count = log.Count, lines = log });
        }

        bool wasOpen = ConsolidatedLauncherForm.TryGetOpen(folder) != null;
        var form = ConsolidatedLauncherForm.GetOrCreate(
            ctx.Host.CurrentTheme, folder, ShortcutsIn(folder));
        // Same first-probe grace as stop-all: a fresh window hasn't matched
        // externally started processes yet.
        if (!wasOpen && verb is "stop-one" or "restart-one")
            await Task.Delay(4000);
        if (form.IsDisposed) return Fail("Launcher window was closed while the command ran.");

        switch (verb)
        {
            case "launch-one":
                form.RpcLaunchOne(s.Id);
                return Ok($"Launch started for '{s.Name}' in '{display}'. Poll launcher_status for progress.");
            case "stop-one":
            {
                bool stopped = form.RpcStopOne(s.Id);
                return Ok(stopped ? $"Stopped '{s.Name}'." : $"'{s.Name}' had nothing running to stop.");
            }
            case "restart-one":
                form.RpcRestartOne(s.Id);
                return Ok($"Restart started for '{s.Name}' in '{display}'. Poll launcher_status for progress.");
            default:
                return Fail($"Unknown row verb '{verb}'.");
        }
    }

    /// <summary>Find one Consolidated-eligible shortcut by name or id,
    /// optionally scoped to a folder. Ambiguity and misses return an error
    /// message listing what IS available.</summary>
    private static (Shortcut? Shortcut, string? Error) ResolveShortcut(string? requestedFolder, string name)
    {
        var all = ShortcutStore.Load().Where(s => !s.ExcludeFromConsolidated).ToList();
        if (!string.IsNullOrWhiteSpace(requestedFolder))
        {
            var rf = ResolveFolder(requestedFolder);
            if (rf.Error != null) return (null, rf.Error);
            all = all.Where(s => string.Equals(
                ShortcutFolders.Normalize(s.FolderPath), rf.Folder, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var matches = all.Where(s =>
            string.Equals(s.Id, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return (matches[0], null);
        if (matches.Count > 1)
        {
            var homes = matches.Select(m =>
            {
                var f = ShortcutFolders.Normalize(m.FolderPath);
                return $"{m.Name} ({(f.Length == 0 ? "(root)" : f)})";
            });
            return (null, $"Shortcut '{name}' is ambiguous: {string.Join(", ", homes)}. Pass a folder.");
        }

        var available = string.Join(", ", all.Select(s => s.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        return (null, $"No shortcut named '{name}'."
            + (available.Length > 0 ? $" Available: {available}." : ""));
    }

    // ─────────────────────────── folder resolution ───────────────────────────

    private static (string? Folder, string? Error) ResolveFolder(string? requested)
    {
        var candidates = CandidateFolders();
        if (candidates.Count == 0)
            return (null, "No shortcut folders with Consolidated-Launcher-eligible shortcuts exist.");

        if (!string.IsNullOrWhiteSpace(requested))
        {
            string norm = ShortcutFolders.Normalize(requested);

            var exact = candidates.FirstOrDefault(f =>
                string.Equals(f, norm, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return (exact, null);

            // Convenience: match by leaf name ("Backend" → "Work/Backend") when unambiguous.
            var leafMatches = candidates
                .Where(f => string.Equals(Leaf(f), norm, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (leafMatches.Count == 1) return (leafMatches[0], null);
            if (leafMatches.Count > 1)
                return (null, $"Folder '{requested}' is ambiguous: {string.Join(", ", leafMatches)}.");

            return (null, $"No folder '{requested}'. Available: {DescribeList(candidates)}.");
        }

        // No folder given: an open launcher window wins, then a sole candidate.
        var open = ConsolidatedLauncherForm.OpenFolderPaths();
        if (open.Count == 1) return (open[0], null);
        if (open.Count > 1)
            return (null, $"Multiple launcher windows are open — pass a folder: {DescribeList(open)}.");
        if (candidates.Count == 1) return (candidates[0], null);
        return (null, $"Multiple folders exist — pass a folder: {DescribeList(candidates)}.");
    }

    /// <summary>Folders (normalized) containing at least one shortcut that shows
    /// in the Consolidated Launcher (i.e. not flagged "Ignore in Consolidated").</summary>
    private static List<string> CandidateFolders() =>
        ShortcutStore.Load()
            .Where(s => !s.ExcludeFromConsolidated)
            .Select(s => ShortcutFolders.Normalize(s.FolderPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>All shortcuts of a folder, mirroring the Consolidated tab's
    /// "Open" button (the form itself filters ExcludeFromConsolidated).</summary>
    private static List<Shortcut> ShortcutsIn(string folder) =>
        ShortcutStore.Load()
            .Where(s => string.Equals(
                ShortcutFolders.Normalize(s.FolderPath), folder, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string FoldersJson()
    {
        var open = new HashSet<string>(
            ConsolidatedLauncherForm.OpenFolderPaths(), StringComparer.OrdinalIgnoreCase);
        var all = ShortcutStore.Load().Where(s => !s.ExcludeFromConsolidated).ToList();
        var folders = CandidateFolders().Select(f => new
        {
            folder = f.Length == 0 ? "(root)" : f,
            shortcuts = all.Count(s => string.Equals(
                ShortcutFolders.Normalize(s.FolderPath), f, StringComparison.OrdinalIgnoreCase)),
            launcherOpen = open.Contains(f),
        });
        return JsonSerializer.Serialize(new { ok = true, folders });
    }

    // ─────────────────────────── helpers ───────────────────────────

    private static string? ParseFolder(string? payloadJson)
    {
        var folder = ParseString(payloadJson, "folder");
        // "(root)" round-trips from FoldersJson's display name.
        if (string.Equals(folder, "(root)", StringComparison.OrdinalIgnoreCase)) return "";
        return folder;
    }

    internal static string? ParseString(string? payloadJson, string prop)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try { return JsonNode.Parse(payloadJson)?[prop]?.GetValue<string>(); }
        catch (Exception) { return null; }   // malformed JSON or non-string value
    }

    internal static int? ParseInt(string? payloadJson, string prop)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try { return JsonNode.Parse(payloadJson)?[prop]?.GetValue<int>(); }
        catch (Exception) { return null; }
    }

    internal static bool? ParseBool(string? payloadJson, string prop)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try { return JsonNode.Parse(payloadJson)?[prop]?.GetValue<bool>(); }
        catch (Exception) { return null; }
    }

    private static string Leaf(string folder)
    {
        int i = folder.LastIndexOf('/');
        return i < 0 ? folder : folder[(i + 1)..];
    }

    private static string DescribeList(IEnumerable<string> folders) =>
        string.Join(", ", folders.Select(f => f.Length == 0 ? "(root)" : f));

    private static string Ok(string message) =>
        JsonSerializer.Serialize(new { ok = true, message });

    private static string Fail(string message) =>
        JsonSerializer.Serialize(new { ok = false, message });
}
