using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class WebViewRequestPolicyTests
{
    [Theory]
    [InlineData("https://example.com/path?token=secret", "https://example.com/")]
    [InlineData("http://example.com:8080/path", "http://example.com:8080/")]
    [InlineData("file:///private.txt", "https://unknown.invalid/")]
    [InlineData("not a URI", "https://unknown.invalid/")]
    public void CreateSafeOriginRemovesSensitivePathData(string input, string expected)
    {
        Assert.Equal(new Uri(expected), WebViewRequestPolicy.CreateSafeOrigin(input));
    }

    [Fact]
    public void HasSameOriginIgnoresCaseButNotPort()
    {
        Assert.True(WebViewRequestPolicy.HasSameOrigin(
            new Uri("https://EXAMPLE.com/path"),
            new Uri("https://example.com/other")));
        Assert.False(WebViewRequestPolicy.HasSameOrigin(
            new Uri("https://example.com"),
            new Uri("https://example.com:8443")));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  custom agent  ", "custom agent")]
    public void NormalizeUserAgentTrimsOrRemovesEmptyValues(string? input, string? expected)
    {
        Assert.Equal(expected, WebViewRequestPolicy.NormalizeUserAgent(input));
    }
}
