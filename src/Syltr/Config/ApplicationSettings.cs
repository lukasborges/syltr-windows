using System.Text.Json.Serialization;

namespace Syltr.Config;

/// <summary>
/// Contains application-wide settings that do not depend on Windows UI APIs.
/// </summary>
public sealed record ApplicationSettings
{
    [JsonPropertyName("do_not_disturb")]
    public bool DoNotDisturb { get; init; }

    [JsonPropertyName("spell_languages")]
    public IReadOnlyList<string> SpellLanguages { get; init; } = [];
}
