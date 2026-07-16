using System.Security.Cryptography;
using System.Text;

namespace Syltr.Engine;

/// <summary>
/// Converts persisted service IDs into stable WebView2 profile names.
/// </summary>
public static class WebViewProfileName
{
    private const int MaximumLength = 64;
    private const int HashLength = 12;

    public static string FromServiceId(string serviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var normalized = new string(serviceId
            .ToLowerInvariant()
            .Select(character => IsAllowed(character) ? character : '-')
            .ToArray())
            .Trim(' ', '.');
        if (normalized.Length == 0)
        {
            normalized = "service";
        }

        if (normalized.Length <= MaximumLength)
        {
            return normalized;
        }

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(serviceId)))[..HashLength];
        var prefixLength = MaximumLength - HashLength - 1;
        return $"{normalized[..prefixLength]}-{hash}".TrimEnd(' ', '.');
    }

    private static bool IsAllowed(char character) =>
        character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '#' or '@' or '$' or '(' or ')' or '+' or '-' or '_' or '~' or '.' or ' ';
}
