using Syltr.Catalog;

namespace Syltr.Tests.Catalog;

public class ServiceCatalogTests
{
    [Fact]
    public void PreservesReferenceEntriesAndDisplayOrder()
    {
        var expectedKeys = new[]
        {
            "whatsapp", "telegram", "messenger", "slack", "discord", "element", "skype",
            "gmessages", "threema", "groupme", "instagram", "linkedin", "x", "gchat", "teams",
            "gmail", "outlook", "proton", "tuta", "fastmail", "zohomail", "yahoomail", "icloud",
            "gcalendar", "mstodo", "todoist", "trello", "asana", "clickup", "chatgpt", "claude",
            "gemini", "copilot", "deepseek", "perplexity", "grok", "mistral"
        };

        Assert.Equal(expectedKeys, ServiceCatalog.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void PreservesReferenceCategoryOrder()
    {
        var expected = new[]
        {
            ServiceCatalogCategory.Messaging,
            ServiceCatalogCategory.Email,
            ServiceCatalogCategory.Calendar,
            ServiceCatalogCategory.Tasks,
            ServiceCatalogCategory.AI
        };

        Assert.Equal(expected, ServiceCatalog.Categories);
        Assert.All(ServiceCatalog.Entries, entry => Assert.Contains(entry.Category, expected));
    }

    [Fact]
    public void EntriesHaveUniqueKeysAndValidHttpsUrls()
    {
        Assert.Equal(
            ServiceCatalog.Entries.Count,
            ServiceCatalog.Entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ServiceCatalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.True(Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri));
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        });
    }

    [Theory]
    [InlineData("whatsapp", "WhatsApp Web", "https://web.whatsapp.com/")]
    [InlineData("gcalendar", "Google Calendar", "https://calendar.google.com/")]
    [InlineData("chatgpt", "ChatGPT", "https://chatgpt.com/")]
    public void FindsEntryByStableKey(string key, string name, string url)
    {
        var entry = ServiceCatalog.FindByKey(key);

        Assert.NotNull(entry);
        Assert.Equal(name, entry.Name);
        Assert.Equal(url, entry.Url);
    }

    [Fact]
    public void ExcludesVideoConferenceOnlyServices()
    {
        var excluded = new[] { "gmeet", "zoom", "whereby" };

        Assert.All(excluded, key => Assert.Null(ServiceCatalog.FindByKey(key)));
    }
}
