using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Syltr.Engine.Unread;

namespace Syltr.Engine;

/// <summary>
/// Hosts one isolated service profile without exposing CoreWebView2 to the window layer.
/// </summary>
public sealed partial class ServiceViewHost : IDisposable
{
    private readonly WebViewEnvironmentProvider _environmentProvider;
    private readonly Grid _content = new();
    private WebView2 _webView;
    private Uri _homeUri;
    private readonly ServiceViewContentMapping? _contentMapping;
    private readonly string _externalLinkMessageToken = ExternalLinkClickBridge.CreateToken();
    private readonly bool _captureConsole;
    private string? _userAgent;
    private CoreWebView2DevToolsProtocolEventReceiver? _consoleEventReceiver;
    private readonly HashSet<WebViewDownloadTracker> _downloads = [];
    private TaskCompletionSource<bool>? _initialNavigation;
    private CoreWebView2Profile? _profile;
    private bool _initialized;
    private bool _disposed;
    private bool _isForeground = true;
    private ServiceViewState _state = new(ServiceViewStatus.Created, string.Empty, 0, false, false);

    public ServiceViewHost(
        WebViewEnvironmentProvider environmentProvider,
        string serviceId,
        Uri homeUri,
        ServiceViewContentMapping? contentMapping = null,
        string? userAgent = null,
        bool captureConsole = false)
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
        _userAgent = WebViewRequestPolicy.NormalizeUserAgent(userAgent);
        _captureConsole = captureConsole;
    }

    public string ProfileName { get; }

    public bool IsInitialized => _initialized && !_disposed;

    public bool IsForeground => _isForeground;

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

    public event EventHandler<ServiceConsoleMessageReceivedEventArgs>? ConsoleMessageReceived;

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
            await CreateCoreWebViewAsync();
            await ConfigureCoreWebViewAsync();
            AttachCoreEvents();
            _initialized = true;
            UpdateState(ServiceViewStatus.Ready);
            if (navigateHome)
            {
                await NavigateHomeInitiallyAsync();
            }
        }
        catch (Exception exception)
        {
            UpdateState(ServiceViewStatus.Failed, exception.Message);
            throw;
        }
    }

    private async Task CreateCoreWebViewAsync()
    {
        var environment = await _environmentProvider.GetAsync();
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = ProfileName;
        options.IsInPrivateModeEnabled = false;
        await _webView.EnsureCoreWebView2Async(environment, options);
        _profile = _webView.CoreWebView2.Profile;
        ApplyMemoryUsageTarget();
    }

    private async Task ConfigureCoreWebViewAsync()
    {
        _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        if (_captureConsole)
        {
            await TryEnableConsoleCaptureAsync();
        }

        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            ExternalLinkClickBridge.CreateScript(_externalLinkMessageToken));
        ApplyUserAgent();
        ApplyContentMapping();
    }

    private void ApplyUserAgent()
    {
        if (_userAgent is not null)
        {
            _webView.CoreWebView2.Settings.UserAgent = _userAgent;
        }
    }

    private void ApplyContentMapping()
    {
        if (_contentMapping is null)
        {
            return;
        }

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            _contentMapping.HostName,
            _contentMapping.FolderPath,
            CoreWebView2HostResourceAccessKind.DenyCors);
    }

    private void AttachCoreEvents()
    {
        _webView.NavigationStarting += OnNavigationStarting;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
        _webView.CoreWebView2.ProcessFailed += OnProcessFailed;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _webView.CoreWebView2.PermissionRequested += OnPermissionRequested;
        _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
        _webView.CoreWebView2.NotificationReceived += OnNotificationReceived;
        _webView.CoreWebView2.FaviconChanged += OnFaviconChanged;
    }

    private async Task NavigateHomeInitiallyAsync()
    {
        _initialNavigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NavigateHome();
        await _initialNavigation.Task;
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
        _userAgent = WebViewRequestPolicy.NormalizeUserAgent(userAgent);
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

    public void SetForeground(bool isForeground)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isForeground = isForeground;
        if (_initialized)
        {
            ApplyMemoryUsageTarget();
        }
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

    private void ApplyMemoryUsageTarget()
    {
        _webView.CoreWebView2.MemoryUsageTargetLevel = _isForeground
            ? CoreWebView2MemoryUsageTargetLevel.Normal
            : CoreWebView2MemoryUsageTargetLevel.Low;
    }

    private void DetachCoreEvents()
    {
        _webView.NavigationStarting -= OnNavigationStarting;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
        _webView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        _webView.CoreWebView2.PermissionRequested -= OnPermissionRequested;
        _webView.CoreWebView2.DownloadStarting -= OnDownloadStarting;
        _webView.CoreWebView2.NotificationReceived -= OnNotificationReceived;
        _webView.CoreWebView2.FaviconChanged -= OnFaviconChanged;
        if (_consoleEventReceiver is not null)
        {
            _consoleEventReceiver.DevToolsProtocolEventReceived -= OnConsoleMessageReceived;
            _consoleEventReceiver = null;
        }
        foreach (var download in _downloads.ToArray())
        {
            download.Detach();
        }

        _downloads.Clear();
    }

}
