namespace Syltr.Window;

/// <summary>
/// Hosts the native application shell and its page frame.
/// </summary>
public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        Closed += (_, _) => ((App)Microsoft.UI.Xaml.Application.Current).ShutdownNotifications();

        RootFrame.Navigate(typeof(MainPage));
    }

    internal void SetDragRegion(Microsoft.UI.Xaml.UIElement dragRegion) => SetTitleBar(dragRegion);
}
