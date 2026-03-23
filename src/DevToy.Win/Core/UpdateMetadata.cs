using System.Text.Json.Serialization;

namespace DevToy;

record UpdateMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; init; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; init; } = "";
}
