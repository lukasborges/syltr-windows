using Syltr.Config;

namespace Syltr.Tests.Config;

public class ServiceIdGeneratorTests
{
    [Theory]
    [InlineData("WhatsApp", "whatsapp")]
    [InlineData("Microsoft Teams", "microsoft-teams")]
    [InlineData("  Slack (Work)  ", "slack--work")]
    [InlineData("Telegram_2", "telegram-2")]
    [InlineData("áéí", "service")]
    [InlineData("", "service")]
    public void CreatesReferenceCompatibleSlug(string name, string expected)
    {
        var id = ServiceIdGenerator.CreateUnique([], name);

        Assert.Equal(expected, id);
    }

    [Fact]
    public void UsesFirstAvailableNumericSuffix()
    {
        var services = new[]
        {
            CreateService("slack"),
            CreateService("slack-2"),
            CreateService("slack-4")
        };

        var id = ServiceIdGenerator.CreateUnique(services, "Slack");

        Assert.Equal("slack-3", id);
    }

    private static ServiceDefinition CreateService(string id) => new()
    {
        Id = id,
        Name = id,
        Url = "https://example.com"
    };
}
