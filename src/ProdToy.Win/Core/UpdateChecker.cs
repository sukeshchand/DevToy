using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProdToy;

static class UpdateChecker
{
    private static System.Threading.Timer? _timer;
    private static UpdateMetadata? _latestMetadata;

    public static event Action<UpdateMetadata>? UpdateAvailable;

    public static UpdateMetadata? LatestMetadata => _latestMetadata;

    public static void Start()
    {
        // Check immediately, then every hour
        _timer = new System.Threading.Timer(_ => CheckForUpdate(), null, TimeSpan.Zero, TimeSpan.FromHours(1));
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public static void CheckNow() => CheckForUpdate();

    private static void CheckForUpdate()
    {
        try
        {
            var settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.UpdateLocation))
                return;

            string metadataPath = Path.Combine(settings.UpdateLocation, "metadata.json");
            if (!File.Exists(metadataPath))
                return;

            string json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<UpdateMetadata>(json);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Version))
                return;

            bool updateAvailable = IsNewerVersion(metadata.Version, AppVersion.Current);

            // Fire-and-forget: log the enquiry silently
            _ = LogUpdateEnquiryAsync(settings.UpdateLocation, metadata.Version, updateAvailable);

            if (updateAvailable)
            {
                _latestMetadata = metadata;
                UpdateAvailable?.Invoke(metadata);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    private static async Task LogUpdateEnquiryAsync(string updateLocation, string remoteVersion, bool updateAvailable)
    {
        try
        {
            string logsDir = Path.Combine(updateLocation, "_logs");
            Directory.CreateDirectory(logsDir);

            string fileName = $"{DateTime.Now:yyyyMMdd}_UpdateEnquiry.log";
            string logPath = Path.Combine(logsDir, fileName);

            string user = Environment.UserName;
            string machine = Environment.MachineName;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string status = updateAvailable ? "Update Available" : "Up to Date";
            string logLine = $"{timestamp} | User: {user} | PC: {machine} | Current: {AppVersion.Current} | Remote: {remoteVersion} | {status}{Environment.NewLine}";

            byte[] data = Encoding.UTF8.GetBytes(logLine);

            // Retry with delay to handle concurrent file access from multiple instances
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    await fs.WriteAsync(data);
                    await fs.FlushAsync();
                    return;
                }
                catch (IOException)
                {
                    // File likely locked by another instance, wait and retry
                    await Task.Delay(500 * (attempt + 1));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update enquiry log failed: {ex.Message}");
        }
    }

    internal static bool IsNewerVersion(string remote, string local)
    {
        if (Version.TryParse(remote, out var remoteVer) && Version.TryParse(local, out var localVer))
            return remoteVer > localVer;
        return false;
    }
}
