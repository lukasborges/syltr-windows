using Syltr.Engine.Unread;

namespace Syltr.Tests.Engine.Unread;

public class UnreadCountParserTests
{
    [Theory]
    [InlineData("(5) WhatsApp", 5u)]
    [InlineData("Inbox (12) - Gmail", 12u)]
    [InlineData("[3] Slack", 3u)]
    [InlineData("{7} Chat", 7u)]
    [InlineData("5 messages", 5u)]
    public void ExtractsUnreadCount(string title, uint expected)
    {
        Assert.Equal(expected, UnreadCountParser.FromTitle(title));
    }

    [Theory]
    [InlineData("WhatsApp")]
    [InlineData("")]
    [InlineData("Chat (beta)")]
    [InlineData("Inbox 12 - Gmail")]
    public void ReturnsZeroWhenTitleContainsNoSupportedCount(string title)
    {
        Assert.Equal(0u, UnreadCountParser.FromTitle(title));
    }

    [Fact]
    public void AcceptsNullTitle()
    {
        Assert.Equal(0u, UnreadCountParser.FromTitle(null));
    }

    [Theory]
    [InlineData("(99+) Teams", 99u)]
    [InlineData("(4294967295) maximum", uint.MaxValue)]
    [InlineData("(4294967296) overflow", uint.MaxValue)]
    [InlineData("999999999999999999999 messages", uint.MaxValue)]
    public void SaturatesLargeCounts(string title, uint expected)
    {
        Assert.Equal(expected, UnreadCountParser.FromTitle(title));
    }
}
