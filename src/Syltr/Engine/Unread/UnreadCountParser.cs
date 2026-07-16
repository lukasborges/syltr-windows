namespace Syltr.Engine.Unread;

/// <summary>
/// Extracts unread counters commonly exposed in web application page titles.
/// </summary>
public static class UnreadCountParser
{
    public static uint FromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return 0;
        }

        for (var index = 0; index < title.Length; index++)
        {
            if (title[index] is '(' or '[' or '{'
                && TryReadLeadingNumber(title.AsSpan(index + 1), out var count))
            {
                return count;
            }
        }

        return TryReadLeadingNumber(title.AsSpan(), out var leadingCount) ? leadingCount : 0;
    }

    private static bool TryReadLeadingNumber(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        var found = false;

        foreach (var character in text)
        {
            if (character is not (>= '0' and <= '9'))
            {
                break;
            }

            var digit = (uint)(character - '0');
            value = value > (uint.MaxValue - digit) / 10
                ? uint.MaxValue
                : (value * 10) + digit;
            found = true;
        }

        return found;
    }
}
