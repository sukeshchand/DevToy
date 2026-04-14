using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace ProdToy.Setup;

/// <summary>
/// Downloads release assets from the latest GitHub release. Used when
/// ProdToySetup.exe is run standalone (no sibling ProdToy.zip or plugin zips) —
/// fetches everything from the release into a temp dir so Installer can run
/// the normal local-bundle flow against it. Verifies SHA256 when metadata.json
/// advertises one.
/// </summary>
static class BootstrapDownloader
{
    // Use the /latest/download/<asset> URL shortcut which GitHub resolves to
    // whatever the newest release's asset with that name is. This way an old
    // ProdToySetup.exe always fetches the current release, not the one it
    // was built against.
    private const string LatestDownloadBase =
        "https://github.com/sukeshchand/ProdToy/releases/latest/download";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    static BootstrapDownloader()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ProdToySetup/" + AppVersion.Current);
    }

    /// <summary>
    /// Ensures metadata.json + ProdToy.zip + every plugin zip in the manifest
    /// are present next to the installer. If all of them already exist, no-op.
    /// Otherwise downloads the missing pieces into a sibling cache dir
    /// (tmpDir if writeable, else install root) and returns that path so
    /// Installer.Run() can consume the same layout it already understands.
    /// </summary>
    public static async Task<string> EnsureBundleAsync(Action<string> onProgress)
    {
        string installerDir = Path.GetDirectoryName(Application.ExecutablePath)!;
        string siblingMetadata = Path.Combine(installerDir, "metadata.json");
        string siblingHostZip = Path.Combine(installerDir, "ProdToy.zip");

        // If a complete offline bundle sits next to the installer, prefer it.
        if (File.Exists(siblingMetadata) && File.Exists(siblingHostZip))
        {
            onProgress($"Offline bundle detected at {installerDir}");
            return installerDir;
        }

        // Resolve a writeable staging dir. Try %TEMP%\prodtoy-setup-<pid> first.
        string stagingDir = Path.Combine(Path.GetTempPath(),
            $"prodtoy-setup-{Environment.ProcessId}");
        try
        {
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
        }
        catch (Exception ex)
        {
            onProgress($"Note: could not clean old staging dir: {ex.Message}");
        }
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(Path.Combine(stagingDir, "plugins"));

        onProgress($"Fetching latest release manifest from {LatestDownloadBase}");
        onProgress($"Staging into {stagingDir}");

        // 1. metadata.json (latest)
        string metadataUrl = $"{LatestDownloadBase}/metadata.json";
        string metadataDest = Path.Combine(stagingDir, "metadata.json");
        await DownloadFileAsync(metadataUrl, metadataDest, "metadata.json", expectedSha256: "", onProgress);

        // 2. Parse metadata to discover what to fetch + the expected hashes.
        var meta = System.Text.Json.JsonSerializer.Deserialize<BundleMetadata>(
            await File.ReadAllTextAsync(metadataDest));
        if (meta == null)
            throw new InvalidOperationException("Downloaded metadata.json is invalid.");

        onProgress($"Release v{meta.version} — host + {meta.plugins?.Length ?? 0} plugin(s)");
        if (meta.version != AppVersion.Current)
            onProgress($"Note: this installer was built for v{AppVersion.Current}, release has v{meta.version}");

        // 3. Host zip (flat on GitHub Releases — always strip any "plugins/" prefix).
        string hostZipName = string.IsNullOrWhiteSpace(meta.hostZip) ? "ProdToy.zip" : meta.hostZip;
        string hostUrl = $"{LatestDownloadBase}/{Path.GetFileName(hostZipName)}";
        string hostDest = Path.Combine(stagingDir, "ProdToy.zip");
        await DownloadFileAsync(hostUrl, hostDest, "ProdToy.zip", meta.hostSha256 ?? "", onProgress);

        // 4. Each plugin zip. Drop each into plugins\ so Installer.Run finds them
        //    in the same layout as a local publish-to-DeployPath deploy.
        foreach (var p in meta.plugins ?? Array.Empty<BundlePlugin>())
        {
            if (string.IsNullOrWhiteSpace(p.zip)) continue;
            string zipBasename = Path.GetFileName(p.zip);
            string pluginUrl = $"{LatestDownloadBase}/{zipBasename}";
            string pluginDest = Path.Combine(stagingDir, "plugins", zipBasename);
            await DownloadFileAsync(pluginUrl, pluginDest, zipBasename, p.sha256 ?? "", onProgress);
        }

        onProgress("All release assets downloaded and verified.");
        return stagingDir;
    }

    private static async Task DownloadFileAsync(
        string url, string destPath, string label, string expectedSha256, Action<string> onProgress)
    {
        var sw = Stopwatch.StartNew();
        onProgress($"GET {url}");
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            long? contentLength = resp.Content.Headers.ContentLength;
            onProgress($"  {(int)resp.StatusCode} {resp.StatusCode}  {(contentLength.HasValue ? FormatBytes(contentLength.Value) : "?")}");
            resp.EnsureSuccessStatusCode();

            using (var fs = File.Create(destPath))
            {
                await resp.Content.CopyToAsync(fs);
            }
            long actual = new FileInfo(destPath).Length;
            onProgress($"  saved {label}: {FormatBytes(actual)} in {sw.ElapsedMilliseconds}ms");

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string got = ComputeSha256(destPath);
                if (!got.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(destPath); } catch { }
                    string msg = $"SHA256 mismatch for {label}: expected {expectedSha256}, got {got}";
                    onProgress($"  ERROR: {msg}");
                    throw new InvalidOperationException(msg);
                }
                onProgress($"  sha256 verified ({got.Substring(0, 12)}...)");
            }
        }
        catch (Exception ex)
        {
            onProgress($"  FAILED after {sw.ElapsedMilliseconds}ms: {ex.Message}");
            throw;
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.00} MB";
    }

    // Minimal mirror of UpdateMetadata just for setup-side deserialization.
    private sealed record BundleMetadata
    {
        public string version { get; init; } = "";
        public string hostZip { get; init; } = "";
        public string hostSha256 { get; init; } = "";
        public BundlePlugin[]? plugins { get; init; }
    }

    private sealed record BundlePlugin
    {
        public string id { get; init; } = "";
        public string version { get; init; } = "";
        public string zip { get; init; } = "";
        public string sha256 { get; init; } = "";
    }
}
