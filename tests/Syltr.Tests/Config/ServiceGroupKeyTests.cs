using Syltr.Config;

namespace Syltr.Tests.Config;

public sealed class ServiceGroupKeyTests
{
    [Fact]
    public void Groups_instances_that_only_differ_by_query_or_trailing_slash()
    {
        var first = Create("https://EXAMPLE.com/app/?account=one");
        var second = Create("https://example.com/app?account=two");

        Assert.Equal(ServiceGroupKey.FromService(first), ServiceGroupKey.FromService(second));
    }

    [Fact]
    public void Keeps_different_service_paths_separate()
    {
        var mail = Create("https://example.com/mail");
        var calendar = Create("https://example.com/calendar");

        Assert.NotEqual(ServiceGroupKey.FromService(mail), ServiceGroupKey.FromService(calendar));
    }

    private static ServiceDefinition Create(string url) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "Service",
        Url = url
    };
}
