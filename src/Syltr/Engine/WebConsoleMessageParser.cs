using System.Text.Json;

namespace Syltr.Engine;

public static class WebConsoleMessageParser
{
    private const int MaximumMessageLength = 4096;

    public static bool TryParse(
        string profileName,
        string parameterObjectJson,
        out ServiceConsoleMessage message)
    {
        message = default!;
        if (string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(parameterObjectJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(parameterObjectJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var level = root.TryGetProperty("type", out var type)
                ? ParseLevel(type.GetString())
                : ServiceConsoleMessageLevel.Log;
            var text = root.TryGetProperty("args", out var arguments)
                && arguments.ValueKind == JsonValueKind.Array
                    ? string.Join(" ", arguments.EnumerateArray().Select(FormatArgument))
                    : string.Empty;
            text = Truncate(text.Trim());

            var (origin, line, column) = ParseSource(root);
            message = new ServiceConsoleMessage(
                DateTimeOffset.UtcNow,
                profileName,
                level,
                text,
                origin,
                line,
                column);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ServiceConsoleMessageLevel ParseLevel(string? type) => type switch
    {
        "debug" => ServiceConsoleMessageLevel.Debug,
        "info" => ServiceConsoleMessageLevel.Info,
        "warning" => ServiceConsoleMessageLevel.Warning,
        "error" or "assert" => ServiceConsoleMessageLevel.Error,
        _ => ServiceConsoleMessageLevel.Log
    };

    private static string FormatArgument(JsonElement argument)
    {
        if (argument.ValueKind != JsonValueKind.Object)
        {
            return argument.GetRawText();
        }

        if (argument.TryGetProperty("value", out var value))
        {
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.GetRawText();
        }

        if (argument.TryGetProperty("description", out var description)
            && description.ValueKind == JsonValueKind.String)
        {
            return description.GetString() ?? string.Empty;
        }

        return argument.TryGetProperty("type", out var type)
            ? type.GetString() ?? string.Empty
            : string.Empty;
    }

    private static (Uri? Origin, int? Line, int? Column) ParseSource(JsonElement root)
    {
        if (!root.TryGetProperty("stackTrace", out var stackTrace)
            || !stackTrace.TryGetProperty("callFrames", out var callFrames)
            || callFrames.ValueKind != JsonValueKind.Array
            || callFrames.GetArrayLength() == 0)
        {
            return (null, null, null);
        }

        var frame = callFrames[0];
        Uri? origin = null;
        if (frame.TryGetProperty("url", out var urlValue)
            && Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https")
        {
            origin = new Uri(url.GetLeftPart(UriPartial.Authority));
        }

        int? line = frame.TryGetProperty("lineNumber", out var lineValue)
            && lineValue.TryGetInt32(out var zeroBasedLine)
                ? zeroBasedLine + 1
                : null;
        int? column = frame.TryGetProperty("columnNumber", out var columnValue)
            && columnValue.TryGetInt32(out var zeroBasedColumn)
                ? zeroBasedColumn + 1
                : null;
        return (origin, line, column);
    }

    private static string Truncate(string value) => value.Length <= MaximumMessageLength
        ? value
        : string.Concat(value.AsSpan(0, MaximumMessageLength), "…");
}
