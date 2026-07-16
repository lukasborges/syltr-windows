using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ServiceNotificationReceivedEventArgsTests
{
    [Fact]
    public void Only_the_first_interaction_is_accepted()
    {
        var request = new ServiceNotificationReceivedEventArgs(CreateNotification());

        Assert.True(request.Click());
        Assert.False(request.Close());
        Assert.Equal(ServiceNotificationInteraction.Clicked, request.Interaction);
    }

    [Fact]
    public void Notification_keeps_profile_and_sanitized_origin()
    {
        var notification = CreateNotification();

        Assert.Equal("profile-a", notification.ProfileName);
        Assert.Equal("https://example.com/", notification.SenderOrigin.AbsoluteUri);
    }

    private static ServiceNotification CreateNotification() => new(
        "profile-a",
        new Uri("https://example.com/"),
        "New message",
        "Hello",
        "message-1",
        IsSilent: false,
        RequiresInteraction: false);
}
