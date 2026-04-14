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

    /// <summary>Path (relative to manifest) to the host exe zip.</summary>
    [JsonPropertyName("hostZip")]
    public string HostZip { get; init; } = "";

    /// <summary>SHA256 (hex, lowercase) of the host zip. Empty = unverifiable, accept.</summary>
    [JsonPropertyName("hostSha256")]
    public string HostSha256 { get; init; } = "";

    /// <summary>Per-plugin entries from the manifest format.</summary>
    [JsonPropertyName("plugins")]
    public PluginEntry[] Plugins { get; init; } = Array.Empty<PluginEntry>();

    /// <summary>
    /// Runtime-only: the URL or local path the manifest was loaded from. Used by
    /// the updater to resolve relative asset paths for downloads. Not serialized.
    /// </summary>
    [JsonIgnore]
    public string ManifestUrl { get; init; } = "";
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

    /// <summary>SHA256 (hex, lowercase) of the plugin zip. Empty = unverifiable, accept.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
}
