using System.Text.Json.Serialization;

namespace ProdToy;

record UpdateMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; init; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; init; } = "";

    /// <summary>
    /// Path (relative to manifest dir) to the host exe zip. Local-path flow only.
    /// </summary>
    [JsonPropertyName("hostZip")]
    public string HostZip { get; init; } = "";

    /// <summary>
    /// Per-plugin entries from the new manifest format. Empty for old/HTTP metadata.
    /// </summary>
    [JsonPropertyName("plugins")]
    public PluginEntry[] Plugins { get; init; } = Array.Empty<PluginEntry>();

    /// <summary>
    /// HTTP URL to download the bare exe from (set when update source is a URL).
    /// Null/empty when update source is a local/network path.
    /// </summary>
    [JsonIgnore]
    public string DownloadUrl { get; init; } = "";

    /// <summary>
    /// HTTP URL to download the full bundle zip (exe + plugins).
    /// Preferred over DownloadUrl when available.
    /// </summary>
    [JsonIgnore]
    public string BundleDownloadUrl { get; init; } = "";
}

record PluginEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; init; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; init; } = "";

    /// <summary>Path (relative to manifest dir) to the plugin zip.</summary>
    [JsonPropertyName("zip")]
    public string Zip { get; init; } = "";
}
