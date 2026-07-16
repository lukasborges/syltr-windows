namespace Syltr.Config;

/// <summary>
/// Normalizes and validates user-provided service URLs.
/// </summary>
public static class ServiceUrl
{
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var trimmed = input.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"https://{trimmed}";
    }

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal)
            && !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = Normalize(trimmed);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
