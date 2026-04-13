using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace ProdToy;

static class Updater
{
    public record UpdateResult(bool Success, string Message);

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public static UpdateResult Apply()
    {
        try
        {
            var settings = AppSettings.Load();
            string location = UpdateChecker.ResolveUpdateLocation(settings.UpdateLocation);
            var metadata = UpdateChecker.LatestMetadata;

            string installDir = Path.GetDirectoryName(Application.ExecutablePath)!;
            string currentExe = Application.ExecutablePath;
            string pluginsInstallDir = AppPaths.PluginsBinDir;
            int currentPid = Environment.ProcessId;

            bool isHttp = location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                       || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            // Re-read the freshest metadata directly (don't trust the cached one).
            UpdateMetadata? freshMetadata = LoadFreshMetadata(location, isHttp);
            if (freshMetadata == null)
                return new UpdateResult(false, $"metadata.json not found or invalid at {location}");

            // Prepare a clean tmp working dir under ~/.prod-toy/tmp/.
            string tmpRoot = AppPaths.TmpDir;
            if (Directory.Exists(tmpRoot))
            {
                try { Directory.Delete(tmpRoot, recursive: true); }
                catch (Exception ex) { Debug.WriteLine($"Failed to clean tmp: {ex.Message}"); }
            }
            Directory.CreateDirectory(tmpRoot);

            // Stage host zip → tmp/host/ProdToy.exe (only if a newer host is published).
            bool hostNeedsUpdate = !string.IsNullOrWhiteSpace(freshMetadata.Version)
                && IsNewerVersion(freshMetadata.Version, AppVersion.Current);
            string stagedHostExe = "";
            if (hostNeedsUpdate && !string.IsNullOrWhiteSpace(freshMetadata.HostZip))
            {
                string hostStaging = Path.Combine(tmpRoot, "host");
                Directory.CreateDirectory(hostStaging);

                try
                {
                    FetchAndExtractZip(isHttp, location, freshMetadata.ManifestUrl,
                        freshMetadata.HostZip, hostStaging);
                }
                catch (Exception ex)
                {
                    return new UpdateResult(false, $"Host zip fetch failed: {ex.Message}");
                }

                stagedHostExe = Path.Combine(hostStaging, "ProdToy.exe");
                if (!File.Exists(stagedHostExe))
                {
                    string? found = FindFileRecursive(hostStaging, "ProdToy.exe");
                    if (found == null)
                        return new UpdateResult(false, "ProdToy.exe missing inside host zip.");
                    stagedHostExe = found;
                }
            }

            // Stage plugin zips → tmp/plugins/{id}/ — only those that are newer or missing locally.
            var installedVersions = PluginManager.GetInstalledVersions();
            string pluginsStagingRoot = Path.Combine(tmpRoot, "plugins");
            int pluginsStaged = 0;
            foreach (var p in freshMetadata.Plugins ?? Array.Empty<PluginEntry>())
            {
                if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Zip))
                    continue;

                bool localExists = installedVersions.TryGetValue(p.Id, out var localVer);
                bool isNewer = localExists && IsNewerVersion(p.Version, localVer ?? "");
                // Only stage plugins that are installed AND newer remotely.
                // New-but-not-installed plugins are not auto-installed by Update.
                if (!localExists || !isNewer) continue;

                string pluginDest = Path.Combine(pluginsStagingRoot, p.Id);
                Directory.CreateDirectory(pluginDest);

                try
                {
                    FetchAndExtractZip(isHttp, location, freshMetadata.ManifestUrl,
                        p.Zip, pluginDest);
                    pluginsStaged++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Plugin zip fetch failed for {p.Id}: {ex.Message}");
                    // Best-effort: skip this plugin, keep going with others.
                }
            }

            if (!hostNeedsUpdate && pluginsStaged == 0)
                return new UpdateResult(false, "Nothing new to update.");

            // Write the new orchestration PS1 next to the staged files.
            string ps1Path = Path.Combine(tmpRoot, "_update.ps1");
            string ps1 = BuildLocalPs1(
                installDir: installDir,
                currentExe: currentExe,
                pluginsInstallDir: pluginsInstallDir,
                tmpRoot: tmpRoot,
                stagedHostExe: stagedHostExe,        // empty if host not updated
                pluginsStagingRoot: pluginsStagingRoot,
                targetPid: currentPid);
            File.WriteAllText(ps1Path, ps1, Encoding.UTF8);

            try { File.WriteAllText(Path.Combine(installDir, "_updated.marker"), ""); }
            catch { }

            LaunchPs1(ps1Path, tmpRoot);
            return new UpdateResult(true, "Update started. Application will restart.");
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, $"Update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-read metadata.json from the update location. Works for both local and HTTP
    /// (unlike relying on the cached UpdateChecker.LatestMetadata which may be stale).
    /// </summary>
    private static UpdateMetadata? LoadFreshMetadata(string location, bool isHttp)
    {
        try
        {
            string json;
            string manifestUrl = "";
            if (isHttp)
            {
                json = _http.GetStringAsync(location).GetAwaiter().GetResult();
                manifestUrl = location;
            }
            else
            {
                string manifestPath = Path.Combine(location, "metadata.json");
                if (!File.Exists(manifestPath)) return null;
                json = File.ReadAllText(manifestPath);
            }

            var meta = System.Text.Json.JsonSerializer.Deserialize<UpdateMetadata>(json);
            if (meta == null) return null;
            return isHttp ? meta with { ManifestUrl = manifestUrl } : meta;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFreshMetadata failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve a relative asset path (from metadata.json) to a local file, then
    /// extract that zip flat into destDir. For local updates, the source is a
    /// file under {location}; for HTTP updates, the zip is downloaded to a temp
    /// file first, then extracted (and the temp file deleted).
    /// </summary>
    private static void FetchAndExtractZip(
        bool isHttp, string location, string manifestUrl, string relZip, string destDir)
    {
        if (isHttp)
        {
            string tempZip = AssetDownloader
                .DownloadRelativeAssetAsync(manifestUrl, relZip)
                .GetAwaiter().GetResult();
            try
            {
                ZipFile.ExtractToDirectory(tempZip, destDir, overwriteFiles: true);
            }
            finally
            {
                try { File.Delete(tempZip); } catch { }
            }
        }
        else
        {
            string localZip = Path.Combine(location, NormalizeRelative(relZip));
            if (!File.Exists(localZip))
                throw new FileNotFoundException($"Zip not found: {localZip}", localZip);
            ZipFile.ExtractToDirectory(localZip, destDir, overwriteFiles: true);
        }
    }

    private static string? FindFileRecursive(string rootDir, string fileName)
    {
        foreach (var file in Directory.GetFiles(rootDir, fileName, SearchOption.AllDirectories))
            return file;
        return null;
    }

    /// <summary>Manifest paths use forward slashes; convert to OS-native.</summary>
    private static string NormalizeRelative(string relPath) =>
        relPath.Replace('/', Path.DirectorySeparatorChar);

    private static bool IsNewerVersion(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return false;
    }

    private static void LaunchPs1(string scriptPath, string workingDir)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDir,
        });
    }

    /// <summary>
    /// PS1 orchestrator: wait 1s for graceful exit, then 12 retry-kill attempts at 5s
    /// intervals (60s total). If still alive, abort and write update-failed.log so the
    /// user can retry on next launch — installdir/pluginsdir stay untouched.
    /// </summary>
    private static string BuildLocalPs1(
        string installDir, string currentExe, string pluginsInstallDir,
        string tmpRoot, string stagedHostExe, string pluginsStagingRoot, int targetPid)
    {
        return $@"
# ProdToy Auto-Updater (local manifest flow)
# Lives at $tmpRoot\_update.ps1 — self-cleans the entire tmp dir at the end.

$exePath           = '{currentExe.Replace("'", "''")}'
$installDir        = '{installDir.Replace("'", "''")}'
$pluginsDest       = '{pluginsInstallDir.Replace("'", "''")}'
$tmpRoot           = '{tmpRoot.Replace("'", "''")}'
$stagedHostExe     = '{stagedHostExe.Replace("'", "''")}'
$pluginsStagingRoot= '{pluginsStagingRoot.Replace("'", "''")}'
$targetPid         = {targetPid}
$failLog           = Join-Path $tmpRoot 'update-failed.log'

function Write-FailLog($reason) {{
    try {{
        $stamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssZ')
        Add-Content -Path $failLog -Value ""$stamp $reason""
    }} catch {{ }}
}}

# Phase 1: 1-second grace, then retry-kill every 5s up to 60s budget.
Start-Sleep -Milliseconds 1000
$proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
if ($proc) {{
    $killed = $false
    for ($attempt = 1; $attempt -le 12; $attempt++) {{
        try {{ Stop-Process -Id $targetPid -Force -ErrorAction Stop }} catch {{ }}
        Start-Sleep -Seconds 5
        $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
        if (-not $proc) {{ $killed = $true; break }}
    }}
    if (-not $killed) {{
        Write-FailLog ""ABORT: PID $targetPid still alive after 60s. Install dir untouched.""
        exit 1
    }}
}}

# Phase 2: Swap the host exe (only if a new one was staged).
if ($stagedHostExe -and (Test-Path $stagedHostExe)) {{
    try {{
        Copy-Item -Path $stagedHostExe -Destination $exePath -Force
    }} catch {{
        Write-FailLog ""ERROR copying host exe: $($_.Exception.Message)""
        exit 2
    }}
}}

# Phase 3: Deploy each staged plugin dir → PluginsBinDir\{{id}}\
if (Test-Path $pluginsStagingRoot) {{
    if (-not (Test-Path $pluginsDest)) {{ New-Item -ItemType Directory -Path $pluginsDest -Force | Out-Null }}
    foreach ($dir in Get-ChildItem $pluginsStagingRoot -Directory) {{
        $dest = Join-Path $pluginsDest $dir.Name
        if (-not (Test-Path $dest)) {{ New-Item -ItemType Directory -Path $dest -Force | Out-Null }}
        try {{
            Copy-Item ""$($dir.FullName)\*"" $dest -Force -Recurse
        }} catch {{
            Write-FailLog ""ERROR copying plugin $($dir.Name): $($_.Exception.Message)""
        }}
    }}
}}

# Phase 4: Relaunch the host.
if (Test-Path $exePath) {{
    Start-Process -FilePath $exePath
}}

# Phase 5: Self-cleanup of the entire tmp dir.
Start-Sleep -Seconds 2
try {{
    Remove-Item $tmpRoot -Recurse -Force -ErrorAction Stop
}} catch {{ }}
";
    }

    /// <summary>
    /// Ensures the hook script on disk matches this exe's version.
    /// Called on startup so the new exe always writes the latest script.
    /// </summary>
    public static void EnsureHookScript(string exePath)
    {
        try
        {
            string hooksDir = AppPaths.ClaudeHooksDir;
            string ps1Path = Path.Combine(hooksDir, "Show-ProdToy.ps1");

            Directory.CreateDirectory(hooksDir);

            string ps1Content = $@"[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$inputJson = [Console]::In.ReadToEnd()

$title     = ""ProdToy""
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
            # Save question to history via ProdToy and exit
            if ($payload.prompt) {{
                $qFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ""prodtoy_question.txt"")
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
            $title = ""ProdToy - Done""
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

$msgFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ""prodtoy_msg.txt"")
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
