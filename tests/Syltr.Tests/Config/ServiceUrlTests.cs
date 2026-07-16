using Syltr.Config;

namespace Syltr.Tests.Config;

public class ServiceUrlTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("  example.com/path  ", "https://example.com/path")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("HTTPS://example.com", "HTTPS://example.com")]
    public void NormalizesUserInput(string input, string expected)
    {
        Assert.Equal(expected, ServiceUrl.Normalize(input));
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("http://localhost:8080/app", "http://localhost:8080/app")]
    public void AcceptsValidWebUrls(string input, string expected)
    {
        var isValid = ServiceUrl.TryNormalize(input, out var normalized);

        Assert.True(isValid);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("ftp://example.com")]
    public void RejectsInvalidServiceUrls(string input)
    {
        var isValid = ServiceUrl.TryNormalize(input, out var normalized);

        Assert.False(isValid);
        Assert.Empty(normalized);
    }
}
