using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ServiceDownloadRequestedEventArgsTests
{
    [Fact]
    public void Constructor_exposes_only_the_source_origin()
    {
        var request = new ServiceDownloadRequestedEventArgs(
            "profile-a",
            new Uri("https://user:secret@example.com:8443/private?token=secret"),
            "report.pdf",
            "application/pdf",
            42);

        Assert.Equal("https://example.com:8443/", request.SourceOrigin.AbsoluteUri);
        Assert.Equal(42, request.TotalBytes);
    }

    [Fact]
    public void Save_requires_an_absolute_destination()
    {
        var request = CreateRequest();

        Assert.Throws<ArgumentException>(() => request.SaveTo("relative.txt"));
        Assert.False(request.IsDecided);
    }

    [Fact]
    public void Only_the_first_download_decision_is_accepted()
    {
        var request = CreateRequest();
        var destination = Path.Combine(Path.GetTempPath(), "report.pdf");

        Assert.True(request.SaveTo(destination));
        Assert.False(request.Cancel());
        Assert.Equal(ServiceDownloadDecision.Save, request.Response?.Decision);
        Assert.Equal(Path.GetFullPath(destination), request.Response?.DestinationPath);
    }

    private static ServiceDownloadRequestedEventArgs CreateRequest() => new(
        "profile-a",
        new Uri("https://example.com/file"),
        "report.pdf",
        "application/pdf",
        null);
}
