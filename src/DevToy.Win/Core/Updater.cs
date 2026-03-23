using System.Diagnostics;
using System.Text;

namespace DevToy;

static class Updater
{
    public record UpdateResult(bool Success, string Message);

    public static UpdateResult Apply()
    {
        try
        {
            var settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.UpdateLocation))
                return new UpdateResult(false, "No update location configured.");

            string sourceExe = Path.Combine(settings.UpdateLocation, "DevToy.exe");
            if (!File.Exists(sourceExe))
                return new UpdateResult(false, $"Update file not found at {sourceExe}");

            string installDir = Path.GetDirectoryName(Application.ExecutablePath)!;
            string currentExe = Application.ExecutablePath;
            string updateExe = Path.Combine(installDir, "DevToy.update.exe");
            string batchPath = Path.Combine(installDir, "_update.bat");

            // Step 1: Copy new exe from network to local staging file
            File.Copy(sourceExe, updateExe, overwrite: true);

            // Step 2: Write the updater batch script
            // Note: Hook script regeneration happens on startup (see EnsureHookScript)
            // so the NEW exe updates the script when it launches.
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("timeout /t 2 /nobreak >nul");
            sb.AppendLine($"if exist \"{currentExe}\" del /f /q \"{currentExe}\"");
            sb.AppendLine($"if exist \"{updateExe}\" rename \"{updateExe}\" \"{Path.GetFileName(currentExe)}\"");
            sb.AppendLine($"start \"\" \"{currentExe}\"");
            sb.AppendLine($"del /f /q \"{batchPath}\" & exit");

            File.WriteAllText(batchPath, sb.ToString(), Encoding.ASCII);

            // Step 4: Launch the batch script and exit
            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = installDir,
            });

            return new UpdateResult(true, "Update started. Application will restart.");
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, $"Update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the hook script on disk matches this exe's version.
    /// Called on startup so the new exe always writes the latest script.
    /// </summary>
    public static void EnsureHookScript(string exePath)
    {
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string hooksDir = Path.Combine(userProfile, ".claude", "hooks");
            string ps1Path = Path.Combine(hooksDir, "Show-DevToy.ps1");

            Directory.CreateDirectory(hooksDir);

            string ps1Content = $@"[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$inputJson = [Console]::In.ReadToEnd()

$title     = ""DevToy""
$message   = ""Task finished.""
$type      = ""success""
$sessionId = """"
$cwd       = """"

$exePath = ""{exePath}""

if ($inputJson) {{
    try {{
        $payload = $inputJson | ConvertFrom-Json

        # Extract session context
        if ($payload.session_id) {{ $sessionId = $payload.session_id }}
        if ($payload.cwd)        {{ $cwd = $payload.cwd }}

        if ($payload.hook_event_name -eq ""UserPromptSubmit"") {{
            # Save question to history via DevToy and exit
            if ($payload.prompt) {{
                $qFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ""devtoy_question.txt"")
                [System.IO.File]::WriteAllText($qFile, $payload.prompt, [System.Text.Encoding]::UTF8)
                $qArgs = @(""--save-question"", ""`""$qFile`"""")
                if ($sessionId) {{ $qArgs += ""--session-id"", $sessionId }}
                if ($cwd)       {{ $qArgs += ""--cwd"", ""`""$cwd`"""" }}
                Start-Process -FilePath $exePath -ArgumentList $qArgs -WindowStyle Hidden
            }}
            exit 0
        }}
        elseif ($payload.hook_event_name -eq ""Notification"") {{
            if ($payload.title)   {{ $title = $payload.title }}
            if ($payload.message) {{ $message = $payload.message }}
            $type = ""info""
        }}
        elseif ($payload.hook_event_name -eq ""Stop"") {{
            $title = ""DevToy - Done""
            if ($payload.last_assistant_message) {{
                $message = $payload.last_assistant_message
            }} else {{
                $message = ""Task finished.""
            }}
            $type = ""success""
        }}
    }}
    catch {{ }}
}}

$msgFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ""devtoy_msg.txt"")
[System.IO.File]::WriteAllText($msgFile, $message, [System.Text.Encoding]::UTF8)

$argList = @(""--title"", ""`""$title`"""", ""--message-file"", ""`""$msgFile`"""", ""--type"", $type)
if ($sessionId) {{ $argList += ""--session-id"", $sessionId }}
if ($cwd)       {{ $argList += ""--cwd"", ""`""$cwd`"""" }}
Start-Process -FilePath $exePath -ArgumentList $argList -WindowStyle Hidden";

            File.WriteAllText(ps1Path, ps1Content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to regenerate hook script: {ex.Message}");
        }
    }
}
