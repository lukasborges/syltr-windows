using Syltr.Engine;

namespace Syltr.Tests.Engine;

public class WebViewProfileNameTests
{
    [Theory]
    [InlineData("whatsapp", "whatsapp")]
    [InlineData("Teams Work", "teams work")]
    [InlineData("service/unsafe", "service-unsafe")]
    public void PreservesOrNormalizesSupportedNames(string serviceId, string expected)
    {
        Assert.Equal(expected, WebViewProfileName.FromServiceId(serviceId));
    }

    [Fact]
    public void LongNamesAreShortenedDeterministically()
    {
        var serviceId = new string('a', 100);

        var first = WebViewProfileName.FromServiceId(serviceId);
        var second = WebViewProfileName.FromServiceId(serviceId);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.StartsWith(new string('a', 51), first);
    }

    [Fact]
    public void SimilarLongNamesKeepDistinctHashes()
    {
        var prefix = new string('a', 100);

        Assert.NotEqual(
            WebViewProfileName.FromServiceId(prefix + "one"),
            WebViewProfileName.FromServiceId(prefix + "two"));
    }
}
