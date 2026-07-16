using Syltr.Config.Notifications;

namespace Syltr.Tests.Config.Notifications;

public sealed class WindowsAppNotificationServiceTests
{
    [Fact]
    public void Parses_notification_activation_arguments()
    {
        var id = Guid.NewGuid();

        var parsed = WindowsAppNotificationService.TryParseActivation(
            $"notificationId={id:D}&profile=teams%2Dwork",
            out var activation);

        Assert.True(parsed);
        Assert.Equal(id, activation.NotificationId);
        Assert.Equal("teams-work", activation.ProfileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("profile=teams")]
    [InlineData("notificationId=invalid&profile=teams")]
    [InlineData("notificationId=00000000-0000-0000-0000-000000000000")]
    public void Rejects_incomplete_activation_arguments(string arguments)
    {
        Assert.False(WindowsAppNotificationService.TryParseActivation(arguments, out _));
    }
}
