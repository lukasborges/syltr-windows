using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ServicePermissionRequestedEventArgsTests
{
    [Fact]
    public void Constructor_exposes_only_the_permission_origin()
    {
        var request = new ServicePermissionRequestedEventArgs(
            "service-work",
            new Uri("https://user:secret@example.com:8443/private?token=secret#fragment"),
            ServicePermissionKind.Camera,
            isUserInitiated: true);

        Assert.Equal("https://example.com:8443/", request.Origin.AbsoluteUri);
        Assert.Equal("service-work", request.ProfileName);
        Assert.True(request.IsUserInitiated);
    }

    [Fact]
    public void Allow_records_whether_the_choice_should_be_remembered()
    {
        var request = CreateRequest();

        var accepted = request.Allow(rememberForProfile: false);

        Assert.True(accepted);
        Assert.Equal(
            new ServicePermissionResponse(ServicePermissionDecision.Allow, false),
            request.Response);
    }

    [Fact]
    public void Only_the_first_decision_is_accepted()
    {
        var request = CreateRequest();

        Assert.True(request.Deny());
        Assert.False(request.Allow());
        Assert.Equal(ServicePermissionDecision.Deny, request.Response?.Decision);
    }

    private static ServicePermissionRequestedEventArgs CreateRequest() => new(
        "service-personal",
        new Uri("https://example.com/path"),
        ServicePermissionKind.Microphone,
        isUserInitiated: true);
}
