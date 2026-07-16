using Syltr.Spellcheck;

namespace Syltr.Tests.Spellcheck;

public sealed class WindowsSpellcheckPreferencesTests
{
    [Fact]
    public void Describes_preferred_languages_in_system_order()
    {
        var languages = WindowsSpellcheckPreferences.Describe(["pt-BR", "en_US"]);

        Assert.Equal(["pt-BR", "en-US"], languages.Select(language => language.Id));
        Assert.All(languages, language => Assert.False(string.IsNullOrWhiteSpace(language.DisplayName)));
    }

    [Fact]
    public void Removes_duplicates_and_empty_language_tags()
    {
        var languages = WindowsSpellcheckPreferences.Describe(["en-US", " ", "EN_us"]);

        Assert.Equal("en-US", Assert.Single(languages).Id);
    }

    [Fact]
    public void Preserves_unknown_tags_for_diagnostics()
    {
        var language = Assert.Single(WindowsSpellcheckPreferences.Describe(["x-syltr"]));

        Assert.Equal("x-syltr", language.Id);
        Assert.Equal("x-syltr", language.DisplayName);
    }
}
