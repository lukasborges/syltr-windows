namespace Syltr.Tests;

public sealed class NotificationPresentationSourceTests
{
    [Fact]
    public void In_app_notification_is_only_a_fallback_for_native_delivery()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Syltr",
            "Window",
            "MainPage.xaml.cs"));

        Assert.Contains(
            "if (TryShowWindowsNotification(notification, e))",
            source,
            StringComparison.Ordinal);
        var nativeDelivery = source.IndexOf(
            "if (TryShowWindowsNotification(notification, e))",
            StringComparison.Ordinal);
        var nativeReturn = source.IndexOf("return;", nativeDelivery, StringComparison.Ordinal);
        var fallback = source.IndexOf(
            "ShowInAppNotification(notification, e);",
            nativeDelivery,
            StringComparison.Ordinal);

        Assert.True(nativeReturn > nativeDelivery);
        Assert.True(fallback > nativeReturn);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Syltr.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
