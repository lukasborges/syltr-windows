using Microsoft.UI.Xaml;
using Syltr.Config;
using Syltr.Config.Notifications;
using Syltr.Window;

namespace Syltr;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Microsoft.UI.Xaml.Window? _window;
    private readonly WindowsAppNotificationService _notifications = new();

    public WindowsAppNotificationService Notifications => _notifications;

    internal Microsoft.UI.Xaml.Window? MainWindowInstance => _window;

    /// <summary>
    /// Initializes the application.
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => WriteUnhandledException(args.Exception);
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _notifications.TryRegister();
        _window = new MainWindow();
        _window.Activate();
    }

    internal void ShutdownNotifications() => _notifications.Dispose();

    internal void ActivateMainWindow() => _window?.Activate();

    internal void SetMainWindowDragRegion(UIElement dragRegion)
    {
        if (_window is MainWindow window)
        {
            window.SetDragRegion(dragRegion);
        }
    }

    internal void CloseMainWindow() => _window?.Close();

    private static void WriteUnhandledException(Exception exception)
    {
        try
        {
            var paths = ApplicationDataPaths.ForCurrentUser();
            Directory.CreateDirectory(paths.LogsDirectory);
            File.AppendAllText(
                Path.Combine(paths.LogsDirectory, "unhandled.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never replace the original application failure.
        }
    }
}
