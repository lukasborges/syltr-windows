using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Syltr.Engine.Unread;

namespace Syltr.Engine;

/// <summary>
/// Hosts one isolated service profile without exposing CoreWebView2 to the window layer.
/// </summary>
public sealed class ServiceViewHost : IDisposable
{
    private readonly WebViewEnvironmentProvider _environmentProvider;
    private readonly Grid _content = new();
    private WebView2 _webView;
    private Uri _homeUri;
    private readonly ServiceViewContentMapping? _contentMapping;
    private string? _userAgent;
    private readonly HashSet<DownloadTracker> _downloads = [];
    private TaskCompletionSource<bool>? _initialNavigation;
    private CoreWebView2Profile? _profile;
    private bool _initialized;
    private bool _disposed;
    private ServiceViewState _state = new(ServiceViewStatus.Created, string.Empty, 0, false, false);

    public ServiceViewHost(
        WebViewEnvironmentProvider environmentProvider,
        string serviceId,
        Uri homeUri,
        ServiceViewContentMapping? contentMapping = null,
        string? userAgent = null)
    {
        ArgumentNullException.ThrowIfNull(environmentProvider);
        ArgumentNullException.ThrowIfNull(homeUri);
        if (!homeUri.IsAbsoluteUri || homeUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Service home URL must be an absolute HTTP or HTTPS URL.", nameof(homeUri));
        }

        _environmentProvider = environmentProvider;
        _webView = new WebView2();
        _content.Children.Add(_webView);
        ProfileName = WebViewProfileName.FromServiceId(serviceId);
        _homeUri = homeUri;
        _contentMapping = contentMapping;
        _userAgent = NormalizeUserAgent(userAgent);
    }

    public string ProfileName { get; }

    public bool IsInitialized => _initialized && !_disposed;

    public FrameworkElement Content => _content;

    public ServiceViewState State => _state;

    public event EventHandler<ServiceViewStateChangedEventArgs>? StateChanged;

    public event EventHandler<ServicePopupRequestedEventArgs>? PopupRequested;

    public event EventHandler<ServicePermissionRequestedEventArgs>? PermissionRequested;

    public event EventHandler<ServiceDownloadRequestedEventArgs>? DownloadRequested;

    public event EventHandler<ServiceDownloadStateChangedEventArgs>? DownloadStateChanged;

    public event EventHandler<ServiceNotificationReceivedEventArgs>? NotificationReceived;

    public event EventHandler<ServiceFaviconChangedEventArgs>? FaviconChanged;

    public event EventHandler<ServiceExternalNavigationRequestedEventArgs>? ExternalNavigationRequested;

    public Task InitializeAsync() => InitializeCoreAsync(navigateHome: true);

    private async Task InitializeCoreAsync(bool navigateHome)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        UpdateState(ServiceViewStatus.Initializing);
        try
        {
            var environment = await _environmentProvider.GetAsync();
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = ProfileName;
            controllerOptions.IsInPrivateModeEnabled = false;

            await _webView.EnsureCoreWebView2Async(environment, controllerOptions);
            _profile = _webView.CoreWebView2.Profile;
            if (_userAgent is not null)
            {
                _webView.CoreWebView2.Settings.UserAgent = _userAgent;
            }
            if (_contentMapping is not null)
            {
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    _contentMapping.HostName,
                    _contentMapping.FolderPath,
                    CoreWebView2HostResourceAccessKind.DenyCors);
            }

            _webView.NavigationStarting += OnNavigationStarting;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
            _webView.CoreWebView2.ProcessFailed += OnProcessFailed;
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webView.CoreWebView2.PermissionRequested += OnPermissionRequested;
            _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
            _webView.CoreWebView2.NotificationReceived += OnNotificationReceived;
            _webView.CoreWebView2.FaviconChanged += OnFaviconChanged;
            _initialized = true;
            UpdateState(ServiceViewStatus.Ready);
            if (navigateHome)
            {
                _initialNavigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                NavigateHome();
                await _initialNavigation.Task;
            }
        }
        catch (Exception exception)
        {
            UpdateState(ServiceViewStatus.Failed, exception.Message);
            throw;
        }
    }

    public void NavigateHome()
    {
        EnsureReady();
        _webView.Source = _homeUri;
    }

    public void Navigate(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri || destination.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Destination must be an absolute HTTP or HTTPS URL.", nameof(destination));
        }

        EnsureReady();
        _webView.Source = destination;
    }

    public void UpdateHome(Uri homeUri, bool navigate = true)
    {
        ArgumentNullException.ThrowIfNull(homeUri);
        if (!homeUri.IsAbsoluteUri || homeUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Service home URL must be an absolute HTTP or HTTPS URL.", nameof(homeUri));
        }

        _homeUri = homeUri;
        if (navigate && IsInitialized)
        {
            NavigateHome();
        }
    }

    public void SetUserAgent(string? userAgent, bool reload = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _userAgent = NormalizeUserAgent(userAgent);
        if (!_initialized)
        {
            return;
        }

        _webView.CoreWebView2.Settings.UserAgent = _userAgent ?? string.Empty;
        if (reload)
        {
            Reload();
        }
    }

    public void Reload()
    {
        EnsureReady();
        _webView.Reload();
    }

    public void GoBack()
    {
        EnsureReady();
        if (_webView.CanGoBack)
        {
            _webView.GoBack();
        }
    }

    public void GoForward()
    {
        EnsureReady();
        if (_webView.CanGoForward)
        {
            _webView.GoForward();
        }
    }

    public void SetMuted(bool muted)
    {
        EnsureReady();
        _webView.CoreWebView2.IsMuted = muted;
    }

    public async Task RecoverAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state.Status != ServiceViewStatus.Failed)
        {
            return;
        }

        var recoveryAction = _state.RecoveryAction;
        UpdateState(ServiceViewStatus.Recovering);
        if (recoveryAction == ServiceViewRecoveryAction.Reload)
        {
            try
            {
                _initialNavigation = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _webView.Reload();
                await _initialNavigation.Task;
                return;
            }
            catch
            {
                // Recreating the control is the fallback when reload cannot recover it.
            }
        }

        await RecreateWebViewAsync(navigateHome: true);
    }

    public void Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            try
            {
                DetachCoreEvents();
                _webView.Close();
            }
            catch
            {
                // Closing an already failed WebView still leaves the service suspended.
            }
        }

        _initialNavigation?.TrySetCanceled();
        _initialized = false;
        _content.Children.Clear();
        UpdateState(ServiceViewStatus.Disabled);
    }

    public Task ResumeAsync(bool navigateHome = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _state.Status == ServiceViewStatus.Disabled
            ? RecreateWebViewAsync(navigateHome)
            : Task.CompletedTask;
    }

    public void DeleteProfile()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var profile = _profile;
        if (_initialized)
        {
            DetachCoreEvents();
        }
        _initialNavigation?.TrySetCanceled();
        _initialized = false;
        _disposed = true;
        try
        {
            _webView.Close();
        }
        catch
        {
            // A suspended or failed WebView may already be closed.
        }

        profile?.Delete();
        UpdateState(ServiceViewStatus.Closed, "Profile deleted.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            try
            {
                DetachCoreEvents();
                _webView.Close();
            }
            catch
            {
                // A fatal browser-process failure may have closed the control already.
            }
        }

        _initialNavigation?.TrySetCanceled();
        _initialized = false;
        _disposed = true;
        UpdateState(ServiceViewStatus.Closed);
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.IsUserInitiated &&
            !args.IsRedirected &&
            Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) &&
            destination.Scheme is "http" or "https" &&
            !HasSameOrigin(_homeUri, destination))
        {
            args.Cancel = true;
            var handler = ExternalNavigationRequested;
            if (handler is not null)
            {
                handler.Invoke(
                    this,
                    new ServiceExternalNavigationRequestedEventArgs(ProfileName, destination));
            }

            UpdateState(ServiceViewStatus.Ready);
            return;
        }

        UpdateState(ServiceViewStatus.Navigating);
    }

    internal async Task<string> ExecuteScriptAsync(string script)
    {
        EnsureReady();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return await _webView.ExecuteScriptAsync(script);
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            UpdateState(ServiceViewStatus.Ready);
            _initialNavigation?.TrySetResult(true);
            return;
        }

        var message = $"Navigation failed: {args.WebErrorStatus}";
        UpdateState(ServiceViewStatus.Failed, message);
        _initialNavigation?.TrySetException(new InvalidOperationException(message));
    }

    private void OnDocumentTitleChanged(CoreWebView2 sender, object args) =>
        UpdateState(_state.Status);

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        var failureKind = args.ProcessFailedKind switch
        {
            CoreWebView2ProcessFailedKind.BrowserProcessExited => ServiceViewProcessFailureKind.BrowserExited,
            CoreWebView2ProcessFailedKind.RenderProcessExited => ServiceViewProcessFailureKind.RendererExited,
            CoreWebView2ProcessFailedKind.RenderProcessUnresponsive => ServiceViewProcessFailureKind.RendererUnresponsive,
            _ => ServiceViewProcessFailureKind.NonFatal
        };
        var recoveryAction = ServiceViewRecoveryPolicy.ForProcessFailure(failureKind);
        if (recoveryAction == ServiceViewRecoveryAction.None)
        {
            return;
        }

        var message = $"WebView2 process failed: {args.ProcessFailedKind} ({args.Reason})";
        UpdateState(ServiceViewStatus.Failed, message, recoveryAction);
        _initialNavigation?.TrySetException(new InvalidOperationException(message));
    }

    private async void OnNewWindowRequested(
        CoreWebView2 sender,
        CoreWebView2NewWindowRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        ServiceViewHost? popupHost = null;
        try
        {
            popupHost = new ServiceViewHost(
                _environmentProvider,
                ProfileName,
                _homeUri,
                _contentMapping,
                _userAgent);
            var requestedUri = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ? uri : null;
            var request = new ServicePopupRequestedEventArgs(
                popupHost,
                requestedUri,
                args.IsUserInitiated,
                requestedUri is not null && HasSameOrigin(_homeUri, requestedUri));
            var handler = PopupRequested;
            if (handler is null)
            {
                request.Cancel();
            }
            else
            {
                handler.Invoke(this, request);
            }

            var disposition = await request.WaitForDecisionAsync();
            if (disposition is not ServicePopupDisposition.InApp)
            {
                args.Handled = true;
                popupHost.Dispose();
                return;
            }

            await popupHost.InitializeCoreAsync(navigateHome: false);
            args.NewWindow = popupHost._webView.CoreWebView2;
            args.Handled = true;
        }
        catch
        {
            args.Handled = true;
            popupHost?.Dispose();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnPermissionRequested(
        CoreWebView2 sender,
        CoreWebView2PermissionRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var origin = CreateSafeOrigin(args.Uri);
            var request = new ServicePermissionRequestedEventArgs(
                ProfileName,
                origin,
                MapPermissionKind(args.PermissionKind),
                args.IsUserInitiated);

            var handler = PermissionRequested;
            if (handler is null)
            {
                request.Deny(rememberForProfile: false);
            }
            else
            {
                handler.Invoke(this, request);
            }

            var response = await request.WaitForDecisionAsync();
            args.State = response.Decision == ServicePermissionDecision.Allow
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            args.SavesInProfile = response.RememberForProfile;
            args.Handled = true;
        }
        catch
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.SavesInProfile = false;
            args.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnDownloadStarting(
        CoreWebView2 sender,
        CoreWebView2DownloadStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var operation = args.DownloadOperation;
            var suggestedFileName = Path.GetFileName(args.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggestedFileName))
            {
                suggestedFileName = "download";
            }

            var request = new ServiceDownloadRequestedEventArgs(
                ProfileName,
                CreateSafeOrigin(operation.Uri),
                suggestedFileName,
                operation.MimeType,
                operation.TotalBytesToReceive);
            var handler = DownloadRequested;
            if (handler is null)
            {
                request.Cancel();
            }
            else
            {
                handler.Invoke(this, request);
            }

            var response = await request.WaitForDecisionAsync();
            args.Handled = true;
            if (response.Decision == ServiceDownloadDecision.Cancel ||
                string.IsNullOrWhiteSpace(response.DestinationPath))
            {
                args.Cancel = true;
                return;
            }

            args.ResultFilePath = response.DestinationPath;
            var tracker = new DownloadTracker(this, operation, response.DestinationPath);
            _downloads.Add(tracker);
            tracker.Attach();
        }
        catch
        {
            args.Cancel = true;
            args.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnNotificationReceived(
        CoreWebView2 sender,
        CoreWebView2NotificationReceivedEventArgs args)
    {
        var handler = NotificationReceived;
        if (handler is null)
        {
            return;
        }

        var webNotification = args.Notification;
        args.Handled = true;
        webNotification.ReportShown();
        var notification = new ServiceNotification(
            ProfileName,
            CreateSafeOrigin(args.SenderOrigin),
            webNotification.Title ?? string.Empty,
            webNotification.Body ?? string.Empty,
            webNotification.Tag ?? string.Empty,
            webNotification.IsSilent,
            webNotification.RequiresInteraction);
        try
        {
            handler.Invoke(
                this,
                new ServiceNotificationReceivedEventArgs(
                    notification,
                    webNotification.ReportClicked,
                    webNotification.ReportClosed));
        }
        catch
        {
            try
            {
                webNotification.ReportClosed();
            }
            catch
            {
                // The browser may close while the native handler is unwinding.
            }
        }
    }

    private async void OnFaviconChanged(CoreWebView2 sender, object args)
    {
        if (FaviconChanged is null)
        {
            return;
        }

        try
        {
            using var randomAccessStream = await sender.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            using var stream = randomAccessStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length is > 0 and <= 1024 * 1024)
            {
                FaviconChanged?.Invoke(this, new ServiceFaviconChangedEventArgs(buffer.ToArray()));
            }
        }
        catch
        {
            // Initials remain visible when a page has no usable favicon.
        }
    }

    private static Uri CreateSafeOrigin(string requestedUri)
    {
        if (Uri.TryCreate(requestedUri, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            return new Uri(uri.GetLeftPart(UriPartial.Authority));
        }

        return new Uri("https://unknown.invalid");
    }

    private static bool HasSameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static string? NormalizeUserAgent(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();

    private static ServicePermissionKind MapPermissionKind(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Microphone => ServicePermissionKind.Microphone,
        CoreWebView2PermissionKind.Camera => ServicePermissionKind.Camera,
        CoreWebView2PermissionKind.Geolocation => ServicePermissionKind.Geolocation,
        CoreWebView2PermissionKind.Notifications => ServicePermissionKind.Notifications,
        CoreWebView2PermissionKind.OtherSensors => ServicePermissionKind.OtherSensors,
        CoreWebView2PermissionKind.ClipboardRead => ServicePermissionKind.ClipboardRead,
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => ServicePermissionKind.MultipleAutomaticDownloads,
        CoreWebView2PermissionKind.FileReadWrite => ServicePermissionKind.FileReadWrite,
        CoreWebView2PermissionKind.Autoplay => ServicePermissionKind.Autoplay,
        CoreWebView2PermissionKind.LocalFonts => ServicePermissionKind.LocalFonts,
        CoreWebView2PermissionKind.MidiSystemExclusiveMessages => ServicePermissionKind.MidiSystemExclusiveMessages,
        CoreWebView2PermissionKind.WindowManagement => ServicePermissionKind.WindowManagement,
        _ => ServicePermissionKind.Unknown
    };

    private async Task RecreateWebViewAsync(bool navigateHome)
    {
        try
        {
            if (_initialized)
            {
                try
                {
                    DetachCoreEvents();
                    _webView.Close();
                }
                catch
                {
                    // A browser-process failure may have already closed the control.
                }
            }

            _initialized = false;
            _initialNavigation = null;
            _content.Children.Clear();
            _webView = new WebView2();
            _content.Children.Add(_webView);
            await InitializeCoreAsync(navigateHome);
        }
        catch (Exception exception)
        {
            UpdateState(
                ServiceViewStatus.Failed,
                $"WebView2 recovery failed: {exception.Message}",
                ServiceViewRecoveryAction.Recreate);
            throw;
        }
    }

    private void UpdateState(
        ServiceViewStatus status,
        string? errorMessage = null,
        ServiceViewRecoveryAction recoveryAction = ServiceViewRecoveryAction.None)
    {
        var title = string.Empty;
        var canGoBack = false;
        var canGoForward = false;
        if (_initialized)
        {
            try
            {
                title = _webView.CoreWebView2.DocumentTitle ?? string.Empty;
                canGoBack = _webView.CanGoBack;
                canGoForward = _webView.CanGoForward;
            }
            catch
            {
                title = _state.DocumentTitle;
            }
        }

        _state = new ServiceViewState(
            status,
            title,
            UnreadCountParser.FromTitle(title),
            canGoBack,
            canGoForward,
            errorMessage,
            recoveryAction);
        StateChanged?.Invoke(this, new ServiceViewStateChangedEventArgs(_state));
    }

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("The service view has not been initialized.");
        }
    }

    private void DetachCoreEvents()
    {
        _webView.NavigationStarting -= OnNavigationStarting;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
        _webView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        _webView.CoreWebView2.PermissionRequested -= OnPermissionRequested;
        _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
        _webView.CoreWebView2.NotificationReceived -= OnNotificationReceived;
        _webView.CoreWebView2.FaviconChanged -= OnFaviconChanged;
        foreach (var download in _downloads.ToArray())
        {
            download.Detach();
        }

        _downloads.Clear();
    }

    private sealed class DownloadTracker(
        ServiceViewHost owner,
        CoreWebView2DownloadOperation operation,
        string destinationPath)
    {
        private readonly Guid _id = Guid.NewGuid();
        private bool _attached;

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            operation.BytesReceivedChanged += OnProgressChanged;
            operation.StateChanged += OnStateChanged;
            _attached = true;
            Publish();
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            operation.BytesReceivedChanged -= OnProgressChanged;
            operation.StateChanged -= OnStateChanged;
            _attached = false;
        }

        private void OnProgressChanged(CoreWebView2DownloadOperation sender, object args) => Publish();

        private void OnStateChanged(CoreWebView2DownloadOperation sender, object args)
        {
            Publish();
            if (operation.State is not CoreWebView2DownloadState.InProgress)
            {
                Detach();
                owner._downloads.Remove(this);
            }
        }

        private void Publish()
        {
            var status = operation.State switch
            {
                CoreWebView2DownloadState.Completed => ServiceDownloadStatus.Completed,
                CoreWebView2DownloadState.Interrupted => ServiceDownloadStatus.Interrupted,
                _ => ServiceDownloadStatus.InProgress
            };
            long? totalBytes = operation.TotalBytesToReceive > 0
                ? operation.TotalBytesToReceive
                : null;
            owner.DownloadStateChanged?.Invoke(
                owner,
                new ServiceDownloadStateChangedEventArgs(new ServiceDownloadState(
                    _id,
                    owner.ProfileName,
                    Path.GetFileName(destinationPath),
                    destinationPath,
                    Math.Max(0, operation.BytesReceived),
                    totalBytes,
                    status,
                    status == ServiceDownloadStatus.Interrupted
                        ? operation.InterruptReason.ToString()
                        : null)));
        }
    }
}
