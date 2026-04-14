using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace ProdToy.Setup;

/// <summary>
/// Performs the actual install/repair/update work. Extracts bundled zips next
/// to the running installer into the user's .prod-toy directory, writes the
/// Claude hook script, merges Claude settings, and registers the app in
/// Windows "Apps &amp; Features".
/// </summary>
static class Installer
{
    public record InstallResult(bool Success, string Message);

    /// <summary>
    /// Default bundle location: next to the running installer exe. Used when
    /// Run() is called without a bundleDir (e.g. offline install with zips
    /// shipped alongside ProdToySetup.exe).
    /// </summary>
    public static string DefaultBundleDir => Path.GetDirectoryName(Application.ExecutablePath)!;

    public static string DefaultMetadataPath => Path.Combine(DefaultBundleDir, "metadata.json");

    /// <summary>
    /// Returns the version of the bundled host (from metadata.json next to the
    /// installer if present, otherwise falls back to the installer's own
    /// AppVersion.Current). Used by SetupForm for display BEFORE bootstrap
    /// download runs, so it can only see a sibling metadata.json.
    /// </summary>
    public static string ReadBundledVersion()
    {
        try
        {
            if (File.Exists(DefaultMetadataPath))
            {
                var json = JsonNode.Parse(File.ReadAllText(DefaultMetadataPath));
                var v = json?["version"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ReadBundledVersion failed: {ex.Message}");
        }
        return AppVersion.Current;
    }

    /// <summary>
    /// Runs the install against the given bundle directory. The directory must
    /// contain ProdToy.zip, metadata.json, and a plugins\*.zip subdir — either
    /// shipped alongside the installer or assembled by BootstrapDownloader.
    /// </summary>
    public static InstallResult Run(string bundleDir, Action<string> onProgress)
    {
        string hostZipPath = Path.Combine(bundleDir, "ProdToy.zip");
        string pluginsBundleDir = Path.Combine(bundleDir, "plugins");
        string metadataPath = Path.Combine(bundleDir, "metadata.json");

        var log = new StringBuilder();
        void Report(string msg)
        {
            log.AppendLine(msg);
            try { onProgress(msg); } catch { }
        }

        try
        {
            // Step 1: Kill any running ProdToy instances (except this installer).
            Report("Stopping any running ProdToy instances...");
            int currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName("ProdToy"))
            {
                if (proc.Id == currentPid) continue;
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                    Report($"  Closed ProdToy PID {proc.Id}");
                }
                catch (Exception ex)
                {
                    Report($"  Warning: could not kill PID {proc.Id}: {ex.Message}");
                }
            }

            // Step 2: Ensure install dir exists.
            Directory.CreateDirectory(AppPaths.Root);
            Report($"Install directory: {AppPaths.Root}");

            // Step 3: Extract ProdToy.zip → Root\ProdToy.exe
            if (!File.Exists(hostZipPath))
                return new InstallResult(false, $"ProdToy.zip not found at {hostZipPath}.");

            Report($"Extracting host exe from {Path.GetFileName(hostZipPath)}...");
            ExtractZipFlat(hostZipPath, AppPaths.Root);
            Report($"  Host exe → {AppPaths.ExePath}");

            // Step 4: Extract each plugin zip → Root\plugins\bin\{PluginId}\
            int pluginCount = 0;
            if (Directory.Exists(pluginsBundleDir))
            {
                Directory.CreateDirectory(AppPaths.PluginsBinDir);
                foreach (var zipPath in Directory.GetFiles(pluginsBundleDir, "*.zip"))
                {
                    string pluginId = Path.GetFileNameWithoutExtension(zipPath);
                    string destDir = Path.Combine(AppPaths.PluginsBinDir, pluginId);
                    Directory.CreateDirectory(destDir);
                    ExtractZipFlat(zipPath, destDir);
                    Report($"  Plugin {pluginId} → {destDir}");
                    pluginCount++;
                }
                Report($"Installed {pluginCount} plugin package(s).");
            }
            else
            {
                Report("No bundled plugins directory found (skipping plugin install).");
            }

            // Step 5: Copy the installer exe itself to install dir so Windows
            //         Add/Remove Programs can find it for uninstall.
            try
            {
                string runningSetup = Application.ExecutablePath;
                if (!string.Equals(runningSetup, AppPaths.SetupExePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(runningSetup, AppPaths.SetupExePath, overwrite: true);
                    Report($"Copied installer to {AppPaths.SetupExePath}");
                }
            }
            catch (Exception ex)
            {
                Report($"Warning: could not copy installer: {ex.Message}");
            }

            // Step 6: Write the hook script from embedded resource.
            Directory.CreateDirectory(AppPaths.ClaudeHooksDir);
            string ps1Path = Path.Combine(AppPaths.ClaudeHooksDir, "Show-ProdToy.ps1");
            string ps1Content = LoadEmbeddedHookScript(AppPaths.ExePath);
            File.WriteAllText(ps1Path, ps1Content, Encoding.UTF8);
            Report($"Hook script written to {ps1Path}");

            // Step 7: Back up and merge Claude settings.json.
            if (File.Exists(AppPaths.ClaudeSettingsFile))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(
                    Path.GetDirectoryName(AppPaths.ClaudeSettingsFile)!,
                    $"settings.backup_{timestamp}.json");
                try
                {
                    File.Copy(AppPaths.ClaudeSettingsFile, backupPath, overwrite: false);
                    Report($"Backed up Claude settings to {Path.GetFileName(backupPath)}");
                }
                catch { /* backup is best-effort */ }
            }
            MergeHooksIntoSettings();
            Report($"Configured Claude hooks in {AppPaths.ClaudeSettingsFile}");

            // Step 8: Register in Windows Apps & Features using the version from
            //         the bundle's metadata.json (not AppVersion.Current — they
            //         could differ when Setup bootstraps a newer release).
            try
            {
                string bundledVersion = AppVersion.Current;
                try
                {
                    if (File.Exists(metadataPath))
                    {
                        var json = JsonNode.Parse(File.ReadAllText(metadataPath));
                        var v = json?["version"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(v)) bundledVersion = v;
                    }
                }
                catch { }
                AppRegistry.Register(bundledVersion);
                Report($"Registered v{bundledVersion} in Apps & Features.");
            }
            catch (Exception ex)
            {
                Report($"Warning: could not register in Apps & Features: {ex.Message}");
            }

            Report("Installation complete.");
            return new InstallResult(true, log.ToString());
        }
        catch (Exception ex)
        {
            Report($"Error: {ex.Message}");
            return new InstallResult(false, log.ToString());
        }
    }

    /// <summary>
    /// Extract a zip into destDir. Assumes flat zip layout (entries at the root).
    /// Overwrites existing files.
    /// </summary>
    private static void ExtractZipFlat(string zipPath, string destDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // skip directories
            string destPath = Path.Combine(destDir, entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    private static string LoadEmbeddedHookScript(string installExePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("ProdToy.Setup.Scripts.Show-ProdToy.ps1")
            ?? throw new InvalidOperationException("Embedded hook script resource not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string template = reader.ReadToEnd();
        return template.Replace("{{EXE_PATH}}", installExePath);
    }

    private static void MergeHooksIntoSettings()
    {
        string settingsPath = AppPaths.ClaudeSettingsFile;
        JsonNode root;
        if (File.Exists(settingsPath))
        {
            string existing = File.ReadAllText(settingsPath);
            root = JsonNode.Parse(existing) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        var hooksNode = root["hooks"]?.AsObject() ?? new JsonObject();

        string hookCommand =
            $"powershell.exe -ExecutionPolicy Bypass -File \"{AppPaths.ClaudeHooksDir}\\Show-ProdToy.ps1\"";

        var popupHookEntry = new JsonObject
        {
            ["type"] = "command",
            ["command"] = hookCommand,
        };

        MergeHookEvent(hooksNode, "UserPromptSubmit", null, popupHookEntry);
        MergeHookEvent(hooksNode, "Stop", null, popupHookEntry);
        MergeHookEvent(hooksNode, "Notification",
            "permission_prompt|idle_prompt|elicitation_dialog", popupHookEntry);

        root["hooks"] = hooksNode;

        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(settingsPath, root.ToJsonString(options), Encoding.UTF8);
    }

    private static void MergeHookEvent(JsonObject hooksNode, string eventName, string? matcher, JsonObject newHookEntry)
    {
        if (hooksNode[eventName] is JsonArray existingArray)
        {
            // Skip if any existing entry already references Show-ProdToy.
            foreach (var ruleSet in existingArray)
            {
                if (ruleSet?["hooks"] is JsonArray hooksArray)
                {
                    foreach (var hook in hooksArray)
                    {
                        if (hook?["command"]?.GetValue<string>()?.Contains("Show-ProdToy") == true)
                            return;
                    }
                }
            }

            // Otherwise, try to add to a rule set with matching matcher.
            foreach (var ruleSet in existingArray)
            {
                if (ruleSet is not JsonObject ruleObj) continue;
                string? existingMatcher = ruleObj["matcher"]?.GetValue<string>();
                if (existingMatcher == matcher)
                {
                    var hooksArray = ruleObj["hooks"]?.AsArray() ?? new JsonArray();
                    hooksArray.Add(JsonNode.Parse(newHookEntry.ToJsonString()));
                    ruleObj["hooks"] = hooksArray;
                    return;
                }
            }

            // Fall through: create a new rule set.
            var newRuleSet = new JsonObject();
            if (matcher != null) newRuleSet["matcher"] = matcher;
            newRuleSet["hooks"] = new JsonArray { JsonNode.Parse(newHookEntry.ToJsonString()) };
            existingArray.Add(newRuleSet);
        }
        else
        {
            var newRuleSet = new JsonObject();
            if (matcher != null) newRuleSet["matcher"] = matcher;
            newRuleSet["hooks"] = new JsonArray { JsonNode.Parse(newHookEntry.ToJsonString()) };
            hooksNode[eventName] = new JsonArray { newRuleSet };
        }
    }
}
