using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProdToy.Plugins.ClaudeIntegration;

/// <summary>
/// Install/uninstall the status-line integration across one or more Claude
/// installations. Install writes the <c>statusLine</c> entry in each
/// <c>settings.json</c> pointing at the plugin-owned <c>context-bar.ps1</c>.
///
/// The script itself reads <c>ClaudePluginSettings.SlEnabled</c> at runtime
/// and renders empty if the user has toggled it off. That means enabling or
/// disabling the status line at runtime does NOT require touching Claude's
/// settings.json — only toggling the plugin setting.
/// </summary>
static class ClaudeStatusLine
{
    /// <summary>
    /// Extract context-bar.ps1 to the plugin's scripts dir and write the
    /// <c>statusLine</c> entry into every install's <c>settings.json</c>.
    /// Also writes the initial status-line-config.json from the current settings.
    /// </summary>
    public static void Install(
        IEnumerable<ClaudeInstall> installs,
        ClaudePluginSettings settings,
        string pluginSettingsPath)
    {
        try
        {
            Directory.CreateDirectory(ClaudePaths.ScriptsDir);
            ExtractScript(pluginSettingsPath);
            WriteConfig(settings);

            string command = $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"& '{ClaudePaths.ClaudeStatusLineScript}'\"";

            foreach (var install in installs)
                WriteStatusLineEntry(install.SettingsFile, command);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to install status line: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove the <c>statusLine</c> entry from every install's <c>settings.json</c>.
    /// Leaves the PS1 script and config file in place under the plugin data dir.
    /// </summary>
    public static void Uninstall(IEnumerable<ClaudeInstall> installs)
    {
        foreach (var install in installs)
        {
            try
            {
                if (!File.Exists(install.SettingsFile)) continue;
                string json = File.ReadAllText(install.SettingsFile);
                var root = JsonNode.Parse(json);
                if (root is JsonObject obj && obj.ContainsKey("statusLine"))
                {
                    // Only remove if the entry is ours (points at our script).
                    string? cmd = obj["statusLine"]?["command"]?.GetValue<string>();
                    if (cmd != null && cmd.Contains("context-bar.ps1", StringComparison.OrdinalIgnoreCase))
                    {
                        obj.Remove("statusLine");
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        File.WriteAllText(install.SettingsFile, root!.ToJsonString(options), Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to uninstall status line from {install.SettingsFile}: {ex.Message}");
            }
        }
    }

    private static void WriteStatusLineEntry(string settingsPath, string command)
    {
        try
        {
            JsonNode root;
            if (File.Exists(settingsPath))
            {
                string existing = File.ReadAllText(settingsPath);
                root = JsonNode.Parse(existing) ?? new JsonObject();
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                root = new JsonObject();
            }

            var statusLine = new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
            };
            root["statusLine"] = statusLine;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsPath, root.ToJsonString(options), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write statusLine to {settingsPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Write the runtime config file that context-bar.ps1 reads on every render.
    /// Called by <see cref="Install"/> and whenever the user toggles a status-line
    /// item in the settings panel.
    /// </summary>
    public static void WriteConfig(ClaudePluginSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ClaudePaths.ScriptsDir);

            var config = new JsonObject
            {
                ["model"] = settings.SlShowModel,
                ["dir"] = settings.SlShowDir,
                ["branch"] = settings.SlShowBranch,
                ["prompts"] = settings.SlShowPrompts,
                ["context"] = settings.SlShowContext,
                ["duration"] = settings.SlShowDuration,
                ["mode"] = settings.SlShowMode,
                ["version"] = settings.SlShowVersion,
                ["editStats"] = settings.SlShowEditStats,
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ClaudePaths.StatusLineConfigFile, config.ToJsonString(options), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write status line config: {ex.Message}");
        }
    }

    private static void ExtractScript(string pluginSettingsPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("ProdToy.Plugins.ClaudeIntegration.Scripts.context-bar.ps1");
        if (stream == null)
            throw new InvalidOperationException("Embedded context-bar.ps1 resource not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string template = reader.ReadToEnd();

        string content = template
            .Replace("{{SETTINGS_PATH}}", pluginSettingsPath)
            .Replace("{{PIPE_NAME}}", "ProdToy_Pipe");

        File.WriteAllText(ClaudePaths.ClaudeStatusLineScript, content, Encoding.UTF8);
    }
}
