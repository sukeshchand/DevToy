using System.Diagnostics;

namespace ProdToy;

/// <summary>
/// Downloads release artifacts (zips) for the updater and the plugin store.
/// Resolves relative asset paths from metadata.json against the manifest URL,
/// trying sibling-directory layout first (matches local deploys) and then a
/// flat layout (matches GitHub Releases, which is a flat asset namespace).
/// </summary>
static class AssetDownloader
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    static AssetDownloader()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ProdToy/" + AppVersion.Current);
    }

    /// <summary>
    /// Download a relative asset into a temp file and return its path.
    /// Caller is responsible for deleting the temp file when done.
    /// Tries "{manifestDir}/{relPath}" first, then "{manifestDir}/{basename(relPath)}".
    /// Throws if both attempts fail.
    /// </summary>
    public static async Task<string> DownloadRelativeAssetAsync(string manifestUrl, string relPath)
    {
        string baseDir = GetDirectoryUrl(manifestUrl);
        string cleanedRel = relPath.TrimStart('/', '\\').Replace('\\', '/');
        string siblingUrl = baseDir + "/" + cleanedRel;
        string flatUrl = baseDir + "/" + Path.GetFileName(cleanedRel);

        var attempts = siblingUrl == flatUrl
            ? new[] { siblingUrl }
            : new[] { siblingUrl, flatUrl };

        Log.Info($"AssetDownloader: {relPath} → {attempts.Length} attempt(s): {string.Join(" | ", attempts)}");

        Exception? lastError = null;
        foreach (var url in attempts)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Log.Info($"GET {url}");
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                Log.Info($"  status={(int)resp.StatusCode} {resp.StatusCode} len={resp.Content.Headers.ContentLength?.ToString() ?? "?"} in {sw.ElapsedMilliseconds}ms");
                resp.EnsureSuccessStatusCode();

                string tempFile = Path.Combine(Path.GetTempPath(),
                    $"prodtoy_{Guid.NewGuid():N}_{Path.GetFileName(cleanedRel)}");
                using (var fs = File.Create(tempFile))
                {
                    await resp.Content.CopyToAsync(fs);
                }
                var info = new FileInfo(tempFile);
                Log.Info($"  saved {info.Length} bytes to {tempFile} in {sw.ElapsedMilliseconds}ms total");
                return tempFile;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Log.Warn($"GET {url} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
            }
        }

        Log.Error($"All attempts failed for {relPath}", lastError);
        throw new InvalidOperationException(
            $"Failed to download {relPath}. Tried: {string.Join(", ", attempts)}",
            lastError);
    }

    /// <summary>Strip the trailing filename component off a URL, keeping the scheme.</summary>
    private static string GetDirectoryUrl(string url)
    {
        int lastSlash = url.LastIndexOf('/');
        // Don't chop past "https://"
        if (lastSlash < 0 || lastSlash < url.IndexOf("://", StringComparison.Ordinal) + 3)
            return url;
        return url.Substring(0, lastSlash);
    }
}
