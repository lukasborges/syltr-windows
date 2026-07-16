using System.Globalization;

namespace Syltr.Spellcheck;

public sealed record SpellcheckLanguage(string Id, string DisplayName);

/// <summary>
/// Describes the Windows language preferences used by WebView2 spell checking.
/// WebView2 does not expose a supported API for overriding its dictionary list.
/// </summary>
public static class WindowsSpellcheckPreferences
{
    public static Uri SettingsUri { get; } = new("ms-settings:regionlanguage");

    public static IReadOnlyList<SpellcheckLanguage> GetPreferredLanguages() =>
        Describe(Windows.System.UserProfile.GlobalizationPreferences.Languages);

    internal static IReadOnlyList<SpellcheckLanguage> Describe(IEnumerable<string> languageTags)
    {
        ArgumentNullException.ThrowIfNull(languageTags);

        var languages = new List<SpellcheckLanguage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceTag in languageTags)
        {
            if (string.IsNullOrWhiteSpace(sourceTag))
            {
                continue;
            }

            var languageTag = sourceTag.Trim().Replace('_', '-');
            string id;
            string displayName;
            try
            {
                var culture = CultureInfo.GetCultureInfo(languageTag);
                if (string.IsNullOrWhiteSpace(culture.Name))
                {
                    id = languageTag;
                    displayName = languageTag;
                }
                else
                {
                    id = culture.Name;
                    displayName = string.IsNullOrWhiteSpace(culture.NativeName)
                        ? languageTag
                        : culture.NativeName;
                }
            }
            catch (CultureNotFoundException)
            {
                id = languageTag;
                displayName = languageTag;
            }

            if (seen.Add(id))
            {
                languages.Add(new SpellcheckLanguage(id, displayName));
            }
        }

        return languages;
    }
}
