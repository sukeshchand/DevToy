using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProdToy;

/// <summary>
/// `ProdToy.exe --mcp` — a Model Context Protocol stdio server that exposes
/// ProdToy features to MCP clients (Claude Code, etc.). Speaks newline-delimited
/// JSON-RPC 2.0 on stdin/stdout and forwards each tool call to the running
/// ProdToy host over the RPC pipe (<see cref="Program.TrySendRpc"/>).
///
/// The tool surface is DYNAMIC and plugin-owned: plugins register
/// <see cref="ProdToy.Sdk.McpTool"/> descriptors in the host, and this server
/// fetches them live via the host's `mcp.list-tools` / `mcp.help` commands —
/// so new plugin tools (and their documentation) appear here automatically.
/// When the host isn't running, tools/list falls back to the descriptor cache
/// the host mirrors to ~/.prod-toy/mcp-tools.json (and finally to a built-in
/// launcher set), while tool calls return a clear "start ProdToy" error.
///
/// Register in Claude Code with:
///     claude mcp add --scope user prodtoy -- "%USERPROFILE%\.prod-toy\ProdToy.exe" --mcp
/// </summary>
static class McpServerMode
{
    private const string DefaultProtocolVersion = "2024-11-05";

    private sealed record ToolDesc(string Name, string Description, string? SchemaJson);

    private const string FolderSchema =
        """
        {"type":"object","properties":{"folder":{"type":"string","description":"Shortcut folder to target (e.g. \"Work/Backend\" or just the leaf name \"Backend\"). Optional when only one folder exists or one launcher window is open; use launcher_folders to discover names."}},"required":[]}
        """;

    // Fallback descriptors when neither the live host nor the cache is
    // available, and the legacy-command map for hosts older than the dynamic
    // registry (their launcher tools answer on shortcuts.launcher.* only).
    private static readonly (ToolDesc Desc, string LegacyVerb)[] LegacyLauncherTools =
    {
        (new ToolDesc("launcher_restart_all",
            "Stop all processes in a ProdToy Consolidated Launcher folder, wait for them to exit, then launch them all again. " +
            "Use this after making code changes. Launching is asynchronous — poll launcher_status.", FolderSchema), "restart-all"),
        (new ToolDesc("launcher_launch_all",
            "Launch every shortcut in a ProdToy Consolidated Launcher folder. Prefer launcher_restart_all after code changes.", FolderSchema), "launch-all"),
        (new ToolDesc("launcher_stop_all",
            "Stop all running processes in a ProdToy Consolidated Launcher folder ('Keep running on Stop All' shortcuts are left running).", FolderSchema), "stop-all"),
        (new ToolDesc("launcher_status",
            "Live status of a ProdToy Consolidated Launcher folder: per-shortcut state, pid, memory, uptime, Status-URL health.", FolderSchema), "status"),
        (new ToolDesc("launcher_folders",
            "List the ProdToy shortcut folders that can be controlled.", null), "folders"),
    };

    private static readonly ToolDesc HelpTool = new("help",
        "Full ProdToy MCP integration guide: every available tool with usage guidance (served live by the plugins that " +
        "own them), the recommended restart-after-code-change workflow, CLI equivalents, and a CLAUDE.md snippet. " +
        "Call this when unsure how to drive ProdToy.", null);

    public static void Run()
    {
        Log.Info("MCP server mode started");
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { continue; }
            if (msg is null) continue;

            string method = msg["method"]?.GetValue<string>() ?? "";
            JsonNode? id = msg["id"];
            if (id is null) continue;   // notification — nothing to answer

            JsonObject reply;
            try
            {
                JsonNode? result = Handle(method, msg["params"]);
                reply = result is null
                    ? Error(id, -32601, $"Method not found: {method}")
                    : new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = id.DeepClone(),
                        ["result"] = result,
                    };
            }
            catch (Exception ex)
            {
                Log.Error($"MCP request '{method}' failed", ex);
                reply = Error(id, -32603, ex.Message);
            }

            stdout.WriteLine(reply.ToJsonString());
        }
        Log.Info("MCP server mode stopped (stdin closed)");
    }

    /// <summary>Returns the result node for a request, or null for an unknown method.</summary>
    private static JsonNode? Handle(string method, JsonNode? @params) => method switch
    {
        "initialize" => new JsonObject
        {
            ["protocolVersion"] = @params?["protocolVersion"]?.GetValue<string>() ?? DefaultProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "prodtoy",
                ["version"] = AppVersion.Current,
            },
            // Surfaced to the model by MCP clients — the always-current summary
            // of how to drive ProdToy, straight from the exe.
            ["instructions"] =
                "ProdToy is the user's Windows developer utility (tray app): its Consolidated Launcher runs their " +
                "development apps as 'shortcut folders', and plugins add alarms, screenshots, and notifications. " +
                "After making code changes, call launcher_restart_all (optionally with a folder name), then poll " +
                "launcher_status until every shortcut reports Running before testing; never start/stop those apps " +
                "manually. Use launcher_folders to discover folder names, launcher_logs to read an app's console " +
                "output, and the help tool for the full, plugin-served guide to every available tool. " +
                "The ProdToy tray app must be running; if a tool reports it is not, ask the user to start ProdToy.",
        },
        "ping" => new JsonObject(),
        "tools/list" => new JsonObject { ["tools"] = BuildToolList() },
        "tools/call" => CallTool(@params),
        // Empty collections rather than -32601: some clients probe these.
        "resources/list" => new JsonObject { ["resources"] = new JsonArray() },
        "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
        _ => null,
    };

    // ─────────────────────────── tool discovery ───────────────────────────

    /// <summary>Live registry from the running host, else the cache file it
    /// mirrors, else the built-in launcher set. The help tool is always present.</summary>
    private static List<ToolDesc> DiscoverTools()
    {
        var tools = FetchLiveTools() ?? LoadCachedTools()
            ?? LegacyLauncherTools.Select(t => t.Desc).ToList();
        tools.Add(HelpTool);
        return tools;
    }

    private static List<ToolDesc>? FetchLiveTools()
    {
        string? response = Program.TrySendRpc("mcp.list-tools", null);
        if (response is null) return null;
        try
        {
            var node = JsonNode.Parse(response);
            if (node?["ok"]?.GetValue<bool>() != true) return null;   // old host — no registry
            return ParseDescriptors(node["tools"] as JsonArray);
        }
        catch (JsonException) { return null; }
    }

    private static List<ToolDesc>? LoadCachedTools()
    {
        try
        {
            if (!File.Exists(McpToolRegistry.CacheFilePath)) return null;
            var arr = JsonNode.Parse(File.ReadAllText(McpToolRegistry.CacheFilePath)) as JsonArray;
            var list = ParseDescriptors(arr);
            return list is { Count: > 0 } ? list : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"MCP tool cache read failed: {ex.Message}");
            return null;
        }
    }

    private static List<ToolDesc>? ParseDescriptors(JsonArray? arr)
    {
        if (arr is null) return null;
        var list = new List<ToolDesc>();
        foreach (var n in arr)
        {
            string? name = n?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new ToolDesc(name!,
                n?["description"]?.GetValue<string>() ?? "",
                n?["argsSchema"]?.GetValue<string>()));
        }
        return list;
    }

    private static JsonArray BuildToolList()
    {
        var arr = new JsonArray();
        foreach (var t in DiscoverTools())
        {
            JsonNode schema;
            try
            {
                schema = t.SchemaJson != null
                    ? JsonNode.Parse(t.SchemaJson) ?? EmptySchema()
                    : EmptySchema();
            }
            catch (JsonException)
            {
                Log.Warn($"MCP tool '{t.Name}' has malformed argsSchema — using empty schema");
                schema = EmptySchema();
            }
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = schema,
            });
        }
        return arr;
    }

    private static JsonObject EmptySchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["required"] = new JsonArray(),
    };

    // ─────────────────────────── tool calls ───────────────────────────

    private static JsonObject CallTool(JsonNode? @params)
    {
        string name = @params?["name"]?.GetValue<string>() ?? "";
        if (name.Length == 0)
            return ToolResult("tools/call missing 'name'.", isError: true);

        if (name == HelpTool.Name)
            return ToolResult(BuildInfoDocument(), isError: false);

        string? payload = (@params?["arguments"] as JsonObject) is { Count: > 0 } args
            ? args.ToJsonString()
            : null;

        string? response = Program.TrySendRpc($"mcp.tool.{name}", payload);

        // Host predates the dynamic registry (or the plugin re-registered under
        // the legacy command only): fall back to shortcuts.launcher.<verb>.
        if (response != null && IsUnknownCommand(response))
        {
            var legacy = LegacyLauncherTools.FirstOrDefault(t => t.Desc.Name == name);
            if (legacy.LegacyVerb != null)
                response = Program.TrySendRpc($"shortcuts.launcher.{legacy.LegacyVerb}", payload);
        }

        if (response is null)
            return ToolResult(
                "ProdToy is not running (RPC pipe unreachable). Ask the user to start ProdToy, then retry.",
                isError: true);

        bool ok = false;
        try
        {
            using var doc = JsonDocument.Parse(response);
            ok = doc.RootElement.TryGetProperty("ok", out var okProp)
                && okProp.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { }
        return ToolResult(response, isError: !ok);
    }

    private static bool IsUnknownCommand(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.False
                && doc.RootElement.TryGetProperty("message", out var msg)
                && (msg.GetString() ?? "").StartsWith("unknown command", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    // ─────────────────────────── self-description ───────────────────────────

    /// <summary>
    /// The self-describing integration guide. Printed by `ProdToy.exe mcp-info`
    /// and returned by the MCP `help` tool. The core sections are fixed; the
    /// per-tool documentation is fetched LIVE from the running host's registry
    /// (`mcp.help`), where each plugin registered its own tools and help text —
    /// so new plugin features appear here automatically.
    /// </summary>
    internal static string BuildInfoDocument()
    {
        string exe = Environment.ProcessPath ?? @"%USERPROFILE%\.prod-toy\ProdToy.exe";
        var sb = new StringBuilder();

        sb.AppendLine("# ProdToy MCP Server — Integration Guide");
        sb.AppendLine($"Version: {AppVersion.Current}");
        sb.AppendLine($"Executable: {exe}");
        sb.AppendLine();
        sb.AppendLine("## What this is");
        sb.AppendLine("ProdToy is a Windows tray app for developers: its Consolidated Launcher runs a");
        sb.AppendLine("*shortcut folder* (a named group of project shortcuts — dotnet/npm/custom");
        sb.AppendLine("commands) as captured processes with live status, logs and health checks, and");
        sb.AppendLine("plugins add alarms, screenshots and rich notifications. This MCP server exposes");
        sb.AppendLine("those features to a Claude instance — e.g. restart the apps under development");
        sb.AppendLine("right after editing their code, read their console output, or set a reminder.");
        sb.AppendLine();
        sb.AppendLine("## Register in Claude Code (one-time, user scope = all projects)");
        sb.AppendLine($"    claude mcp add --scope user prodtoy -- \"{exe}\" --mcp");
        sb.AppendLine("Verify with `claude mcp list` (expect: prodtoy ✔ Connected). New sessions then");
        sb.AppendLine("get the tools automatically.");
        sb.AppendLine();
        sb.AppendLine("## Requirements");
        sb.AppendLine("- The ProdToy tray app must be RUNNING. Tools answer {\"ok\":false,...} otherwise —");
        sb.AppendLine("  ask the user to start ProdToy, then retry.");
        sb.AppendLine("- Launcher tools take `folder` names from ProdToy's shortcut tree; discover them");
        sb.AppendLine("  with launcher_folders. `folder` is optional when unambiguous, and leaf names");
        sb.AppendLine("  match (\"Backend\" → \"Work/Backend\").");
        sb.AppendLine();

        // Per-tool documentation — served by the plugins through the host.
        string? helpResponse = Program.TrySendRpc("mcp.help", null);
        string? pluginHelp = null;
        if (helpResponse != null)
        {
            try
            {
                var node = JsonNode.Parse(helpResponse);
                if (node?["ok"]?.GetValue<bool>() == true)
                    pluginHelp = node["help"]?.GetValue<string>();
            }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(pluginHelp))
        {
            sb.AppendLine(pluginHelp.TrimEnd());
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Tools");
            sb.AppendLine("(ProdToy is not running — start it and re-run `mcp-info` for the full,");
            sb.AppendLine("plugin-served tool documentation. Known tools from the last run:)");
            foreach (var t in DiscoverTools())
                sb.AppendLine($"- {t.Name}: {t.Description}");
            sb.AppendLine();
        }

        sb.AppendLine("## Recommended workflow after editing code");
        sb.AppendLine("1. Call launcher_restart_all (with the project's folder). It stops everything,");
        sb.AppendLine("   waits for the processes to exit, then starts Launch All.");
        sb.AppendLine("2. Poll launcher_status every few seconds until every shortcut reports state");
        sb.AppendLine("   \"Running\" (and healthy urlHealth where a Status URL is configured).");
        sb.AppendLine("3. Only then run tests / probe URLs. Never start or kill the apps yourself —");
        sb.AppendLine("   going through ProdToy keeps the processes tracked in the launcher UI.");
        sb.AppendLine();
        sb.AppendLine("## CLI equivalents (no MCP registration needed)");
        sb.AppendLine($"    \"{exe}\" launcher <stop-all|launch-all|restart-all|status|folders> [--folder <name>]");
        sb.AppendLine($"    \"{exe}\" --rpc mcp.tool.<tool_name> [--payload <arguments-json>]");
        sb.AppendLine("Prints single-line JSON. Exit codes: 0 ok, 1 command failed, 2 usage error or");
        sb.AppendLine("ProdToy not running.");
        sb.AppendLine();
        sb.AppendLine("## Suggested snippet for a project's CLAUDE.md");
        sb.AppendLine("    ## Restarting the apps");
        sb.AppendLine("    This project runs under ProdToy's Consolidated Launcher (folder: \"<name>\").");
        sb.AppendLine("    After code changes, call prodtoy launcher_restart_all, then poll");
        sb.AppendLine("    launcher_status until Running. Never start/stop the services manually.");
        sb.AppendLine();
        sb.AppendLine("## Getting this guide again");
        sb.AppendLine($"    \"{exe}\" mcp-info");
        sb.AppendLine("The MCP server also serves it via the `help` tool and summarizes it in the");
        sb.AppendLine("`instructions` field of its initialize handshake.");
        return sb.ToString();
    }

    private static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonObject Error(JsonNode id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
