using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ServiceExternalNavigationRequestedEventArgsTests
{
    [Fact]
    public void Native_display_receives_only_the_destination_origin()
    {
        var request = new ServiceExternalNavigationRequestedEventArgs(
            "teams-work",
            new Uri("https://user:secret@example.com/private?token=secret"));

        Assert.Equal("https://example.com/", request.DestinationOrigin?.AbsoluteUri);
        Assert.Equal("teams-work", request.ProfileName);
    }
}
