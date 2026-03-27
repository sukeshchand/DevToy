using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProdToy;

static class ClaudeHookManager
{
    private const string AutoTitleMarkerStart = "<!-- DevToy:AutoTitle:Start -->";
    private const string AutoTitleMarkerEnd = "<!-- DevToy:AutoTitle:End -->";

    private static string ClaudeMdPath => AppPaths.ClaudeMdFile;

    /// <summary>
    /// Enables or disables the auto-title instruction in global CLAUDE.md.
    /// When enabled, Claude will see an instruction to set /title to the folder name at session start.
    /// </summary>
    public static void SetAutoTitleHook(bool enabled)
    {
        try
        {
            if (enabled)
                AddInstruction();
            else
                RemoveInstruction();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set auto-title: {ex.Message}");
        }
    }

    private static void AddInstruction()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClaudeMdPath)!);

        string existing = File.Exists(ClaudeMdPath) ? File.ReadAllText(ClaudeMdPath) : "";

        // Already has the instruction
        if (existing.Contains(AutoTitleMarkerStart))
            return;

        string instruction = $@"
{AutoTitleMarkerStart}
## Session Title
At the very beginning of every new session, before responding to anything, you MUST silently run this command:
/title <current_folder_name>
Where <current_folder_name> is the name of the current working directory (just the folder name, not the full path). Do this automatically without mentioning it to the user.
{AutoTitleMarkerEnd}
";

        string newContent = existing.TrimEnd() + "\n" + instruction;
        File.WriteAllText(ClaudeMdPath, newContent.TrimStart(), Encoding.UTF8);
    }

    private static void RemoveInstruction()
    {
        if (!File.Exists(ClaudeMdPath)) return;

        string content = File.ReadAllText(ClaudeMdPath);
        int startIdx = content.IndexOf(AutoTitleMarkerStart);
        if (startIdx < 0) return;

        int endIdx = content.IndexOf(AutoTitleMarkerEnd);
        if (endIdx < 0) return;

        endIdx += AutoTitleMarkerEnd.Length;

        // Remove the block and any surrounding blank lines
        string before = content[..startIdx].TrimEnd();
        string after = content[endIdx..].TrimStart();

        string result = string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after)
            ? ""
            : (before + "\n" + after).Trim() + "\n";

        File.WriteAllText(ClaudeMdPath, result, Encoding.UTF8);
    }

    /// <summary>
    /// Also clean up any old SessionStart hook from settings.json if it exists.
    /// </summary>
    public static void CleanupOldHook()
    {
        try
        {
            string settingsPath = AppPaths.ClaudeSettingsFile;
            if (!File.Exists(settingsPath)) return;

            string json = File.ReadAllText(settingsPath);
            var root = JsonNode.Parse(json);
            if (root?["hooks"] is not JsonObject hooksNode) return;
            if (hooksNode["SessionStart"] is not JsonArray eventArray) return;

            for (int i = eventArray.Count - 1; i >= 0; i--)
            {
                if (eventArray[i]?["hooks"] is not JsonArray hooksArray) continue;
                for (int j = hooksArray.Count - 1; j >= 0; j--)
                {
                    string? command = hooksArray[j]?["command"]?.GetValue<string>();
                    if (command != null && command.Contains("Set-FolderTitle"))
                        hooksArray.RemoveAt(j);
                }
                if (hooksArray.Count == 0)
                    eventArray.RemoveAt(i);
            }

            if (eventArray.Count == 0)
                hooksNode.Remove("SessionStart");

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsPath, root!.ToJsonString(options), Encoding.UTF8);

            // Delete old script
            string oldScript = Path.Combine(AppPaths.ClaudeHooksDir, "Set-FolderTitle.ps1");
            if (File.Exists(oldScript))
                File.Delete(oldScript);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Cleanup old hook failed: {ex.Message}");
        }
    }
}
