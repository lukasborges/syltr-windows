using System.Reflection;

namespace Syltr.Engine;

internal static class ExternalLinkClickBridge
{
    private const string ResourceName = "Syltr.Engine.WebAppScripts.ExternalLinks.js";
    private const string TokenPlaceholder = "__SYLTR_MESSAGE_TOKEN__";

    public static string CreateToken() => Guid.NewGuid().ToString("N");

    public static string CreateScript(string messageToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageToken);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded external-link script '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace(
            TokenPlaceholder,
            messageToken,
            StringComparison.Ordinal);
    }

    public static bool TryParseMessage(
        string message,
        string expectedToken,
        out Uri destination)
    {
        destination = null!;
        var separator = message.IndexOf('\n');
        if (separator < 0 ||
            !message.AsSpan(0, separator).SequenceEqual(expectedToken) ||
            !Uri.TryCreate(message[(separator + 1)..], UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            return false;
        }

        destination = parsed;
        return true;
    }
}
