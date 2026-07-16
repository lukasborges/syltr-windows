using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ExternalLinkClickBridgeTests
{
    [Fact]
    public void Accepts_http_link_with_the_expected_bridge_token()
    {
        const string token = "document-token";

        var accepted = ExternalLinkClickBridge.TryParseMessage(
            $"{token}\nhttps://example.com/message?id=42",
            token,
            out var destination);

        Assert.True(accepted);
        Assert.Equal("https://example.com/message?id=42", destination.AbsoluteUri);
    }

    [Theory]
    [InlineData("wrong-token\nhttps://example.com/")]
    [InlineData("document-token\njavascript:alert(1)")]
    [InlineData("document-token\nnot-a-uri")]
    public void Rejects_untrusted_or_unsupported_messages(string message)
    {
        Assert.False(ExternalLinkClickBridge.TryParseMessage(
            message,
            "document-token",
            out _));
    }

    [Fact]
    public void Embeds_the_per_host_token_in_the_click_script()
    {
        var script = ExternalLinkClickBridge.CreateScript("document-token");

        Assert.Contains("const messageToken = 'document-token';", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener('click', openClickedLinkExternally, true)", script, StringComparison.Ordinal);
        Assert.Contains("document.querySelector('base[target]')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("__SYLTR_MESSAGE_TOKEN__", script, StringComparison.Ordinal);
    }
}
