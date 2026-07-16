using Microsoft.UI.Windowing;
using Syltr.Engine;
using Windows.Graphics;

namespace Syltr.Window;

/// <summary>
/// Presents an OAuth/SSO popup while browser ownership remains in the Engine.
/// </summary>
public sealed class ServicePopupWindow : Microsoft.UI.Xaml.Window
{
    private readonly ServiceViewHost _host;

    public ServicePopupWindow(ServiceViewHost host, Uri? requestedUri)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Title = requestedUri?.Host is { Length: > 0 } hostName
            ? $"Syltr — {hostName}"
            : "Syltr — Login";
        Content = host.Content;
        AppWindow.Resize(new SizeInt32(900, 720));
        AppWindow.SetPresenter(AppWindowPresenterKind.Default);

        _host.StateChanged += OnStateChanged;
        Closed += OnClosed;
    }

    private void OnStateChanged(object? sender, ServiceViewStateChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.State.DocumentTitle))
        {
            Title = $"Syltr — {e.State.DocumentTitle}";
        }
    }

    private void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        _host.StateChanged -= OnStateChanged;
        _host.Dispose();
        Closed -= OnClosed;
    }
}
