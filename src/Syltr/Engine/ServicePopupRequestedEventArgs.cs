namespace Syltr.Engine;

public enum ServicePopupDisposition
{
    InApp,
    External,
    Cancel
}

/// <summary>
/// Requests a native decision for a new window without exposing WebView2 types.
/// </summary>
public sealed class ServicePopupRequestedEventArgs : EventArgs
{
    private readonly TaskCompletionSource<ServicePopupDisposition> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _decisionLock = new();

    public ServicePopupRequestedEventArgs(
        ServiceViewHost popupHost,
        Uri? requestedUri,
        bool isUserInitiated,
        bool isSameOrigin)
    {
        ArgumentNullException.ThrowIfNull(popupHost);
        PopupHost = popupHost;
        RequestedUri = requestedUri;
        RequestedOrigin = CreateOrigin(requestedUri);
        IsUserInitiated = isUserInitiated;
        IsSameOrigin = isSameOrigin;
    }

    public ServiceViewHost PopupHost { get; }

    /// <summary>
    /// Full target used only when the user explicitly chooses the external browser.
    /// </summary>
    public Uri? RequestedUri { get; }

    public Uri? RequestedOrigin { get; }

    public bool IsUserInitiated { get; }

    public bool IsSameOrigin { get; }

    public ServicePopupDisposition? Disposition { get; private set; }

    public bool OpenInApp() => Decide(ServicePopupDisposition.InApp);

    public bool OpenExternal() => Decide(ServicePopupDisposition.External);

    public bool Cancel() => Decide(ServicePopupDisposition.Cancel);

    internal Task<ServicePopupDisposition> WaitForDecisionAsync() => _completion.Task;

    private bool Decide(ServicePopupDisposition disposition)
    {
        lock (_decisionLock)
        {
            if (Disposition is not null)
            {
                return false;
            }

            Disposition = disposition;
            _completion.SetResult(disposition);
            return true;
        }
    }

    private static Uri? CreateOrigin(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port).Uri;
    }
}
