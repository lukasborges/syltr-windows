using System.Text.Json.Serialization;

namespace Syltr.Config;

/// <summary>
/// Describes one configured service instance and its persisted preferences.
/// </summary>
public sealed record ServiceDefinition
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("muted")]
    public bool Muted { get; init; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }

    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; init; }
}
