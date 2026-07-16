using Microsoft.Web.WebView2.Core;

namespace Syltr.Engine;

internal sealed class WebViewDownloadTracker
{
    private readonly string _profileName;
    private readonly CoreWebView2DownloadOperation _operation;
    private readonly string _destinationPath;
    private readonly Action<ServiceDownloadState> _publish;
    private readonly Action<WebViewDownloadTracker> _completed;
    private readonly Guid _id = Guid.NewGuid();
    private bool _attached;

    public WebViewDownloadTracker(
        string profileName,
        CoreWebView2DownloadOperation operation,
        string destinationPath,
        Action<ServiceDownloadState> publish,
        Action<WebViewDownloadTracker> completed)
    {
        _profileName = profileName;
        _operation = operation;
        _destinationPath = destinationPath;
        _publish = publish;
        _completed = completed;
    }

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _operation.BytesReceivedChanged += OnProgressChanged;
        _operation.StateChanged += OnStateChanged;
        _attached = true;
        PublishState();
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _operation.BytesReceivedChanged -= OnProgressChanged;
        _operation.StateChanged -= OnStateChanged;
        _attached = false;
    }

    private void OnProgressChanged(CoreWebView2DownloadOperation sender, object args) => PublishState();

    private void OnStateChanged(CoreWebView2DownloadOperation sender, object args)
    {
        PublishState();
        if (_operation.State is CoreWebView2DownloadState.InProgress)
        {
            return;
        }

        Detach();
        _completed(this);
    }

    private void PublishState()
    {
        var status = MapStatus(_operation.State);
        _publish(new ServiceDownloadState(
            _id,
            _profileName,
            Path.GetFileName(_destinationPath),
            _destinationPath,
            Math.Max(0, _operation.BytesReceived),
            _operation.TotalBytesToReceive > 0 ? _operation.TotalBytesToReceive : null,
            status,
            status == ServiceDownloadStatus.Interrupted
                ? _operation.InterruptReason.ToString()
                : null));
    }

    private static ServiceDownloadStatus MapStatus(CoreWebView2DownloadState state) => state switch
    {
        CoreWebView2DownloadState.Completed => ServiceDownloadStatus.Completed,
        CoreWebView2DownloadState.Interrupted => ServiceDownloadStatus.Interrupted,
        _ => ServiceDownloadStatus.InProgress
    };
}
