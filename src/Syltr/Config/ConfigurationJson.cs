using System.Text.Json;
using System.Text.Json.Serialization;

namespace Syltr.Config;

internal static class ConfigurationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
