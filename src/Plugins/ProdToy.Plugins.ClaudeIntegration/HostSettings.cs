using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProdToy.Plugins.ClaudeIntegration;

/// <summary>
/// Reads/writes the host's settings.json for notification and history settings.
/// These settings live in the host file because PopupForm reads them directly.
/// </summary>
record HostSettingsData
{
    [JsonPropertyName("notificationsEnabled")]
    public bool NotificationsEnabled { get; init; } = true;

    [JsonPropertyName("notificationMode")]
    public string NotificationMode { get; init; } = "Popup";

    [JsonPropertyName("showQuotes")]
    public bool ShowQuotes { get; init; } = true;

    [JsonPropertyName("historyEnabled")]
    public bool HistoryEnabled { get; init; } = true;
}

static class HostSettings
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static HostSettingsData Load(string appRootPath)
    {
        try
        {
            string path = Path.Combine(appRootPath, "settings.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<HostSettingsData>(json) ?? new();
            }
        }
        catch { }
        return new HostSettingsData();
    }

    public static void Save(string appRootPath, HostSettingsData settings)
    {
        try
        {
            string path = Path.Combine(appRootPath, "settings.json");

            // Read the full JSON, update only our fields, write back
            // This preserves other host settings (theme, font, etc.)
            Dictionary<string, object?>? existing = null;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            }
            existing ??= new Dictionary<string, object?>();

            existing["notificationsEnabled"] = settings.NotificationsEnabled;
            existing["notificationMode"] = settings.NotificationMode;
            existing["showQuotes"] = settings.ShowQuotes;
            existing["historyEnabled"] = settings.HistoryEnabled;

            Directory.CreateDirectory(appRootPath);
            File.WriteAllText(path, JsonSerializer.Serialize(existing, _jsonOptions));
        }
        catch { }
    }
}
