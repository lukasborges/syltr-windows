using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Syltr.Engine;

public sealed partial class ServiceViewHost
{
    private async Task TryEnableConsoleCaptureAsync()
    {
        try
        {
            _consoleEventReceiver = _webView.CoreWebView2
                .GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
            _consoleEventReceiver.DevToolsProtocolEventReceived += OnConsoleMessageReceived;
            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        }
        catch
        {
            if (_consoleEventReceiver is not null)
            {
                _consoleEventReceiver.DevToolsProtocolEventReceived -= OnConsoleMessageReceived;
                _consoleEventReceiver = null;
            }
        }
    }

    private void OnConsoleMessageReceived(
        object? sender,
        CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        if (WebConsoleMessageParser.TryParse(
                ProfileName,
                args.ParameterObjectAsJson,
                out var message))
        {
            ConsoleMessageReceived?.Invoke(
                this,
                new ServiceConsoleMessageReceivedEventArgs(message));
        }
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args) =>
        UpdateState(ServiceViewStatus.Navigating);

    private void OnWebMessageReceived(
        CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        string message;
        try
        {
            message = args.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (!ExternalLinkClickBridge.TryParseMessage(
                message,
                _externalLinkMessageToken,
                out var destination))
        {
            return;
        }

        ExternalNavigationRequested?.Invoke(
            this,
            new ServiceExternalNavigationRequestedEventArgs(ProfileName, destination));
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
        var failureKind = MapFailureKind(args.ProcessFailedKind);
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
            popupHost = CreatePopupHost();
            var request = CreatePopupRequest(popupHost, args);
            RequestPopupDecision(request);

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

    private ServiceViewHost CreatePopupHost() => new(
        _environmentProvider,
        ProfileName,
        _homeUri,
        _contentMapping,
        _userAgent,
        _captureConsole);

    private ServicePopupRequestedEventArgs CreatePopupRequest(
        ServiceViewHost popupHost,
        CoreWebView2NewWindowRequestedEventArgs args)
    {
        var requestedUri = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ? uri : null;
        return new ServicePopupRequestedEventArgs(
            popupHost,
            requestedUri,
            args.IsUserInitiated,
            requestedUri is not null && WebViewRequestPolicy.HasSameOrigin(_homeUri, requestedUri));
    }

    private void RequestPopupDecision(ServicePopupRequestedEventArgs request)
    {
        if (PopupRequested is null)
        {
            request.Cancel();
            return;
        }

        PopupRequested.Invoke(this, request);
    }

    private async void OnPermissionRequested(
        CoreWebView2 sender,
        CoreWebView2PermissionRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = new ServicePermissionRequestedEventArgs(
                ProfileName,
                WebViewRequestPolicy.CreateSafeOrigin(args.Uri),
                WebViewRequestPolicy.MapPermissionKind(args.PermissionKind),
                args.IsUserInitiated);
            RequestPermissionDecision(request);
            ApplyPermissionResponse(args, await request.WaitForDecisionAsync());
        }
        catch
        {
            DenyPermission(args);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RequestPermissionDecision(ServicePermissionRequestedEventArgs request)
    {
        if (PermissionRequested is null)
        {
            request.Deny(rememberForProfile: false);
            return;
        }

        PermissionRequested.Invoke(this, request);
    }

    private static void ApplyPermissionResponse(
        CoreWebView2PermissionRequestedEventArgs args,
        ServicePermissionResponse response)
    {
        args.State = response.Decision == ServicePermissionDecision.Allow
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
        args.SavesInProfile = response.RememberForProfile;
        args.Handled = true;
    }

    private static void DenyPermission(CoreWebView2PermissionRequestedEventArgs args)
    {
        args.State = CoreWebView2PermissionState.Deny;
        args.SavesInProfile = false;
        args.Handled = true;
    }

    private async void OnDownloadStarting(
        CoreWebView2 sender,
        CoreWebView2DownloadStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = CreateDownloadRequest(args);
            RequestDownloadDecision(request);
            var response = await request.WaitForDecisionAsync();
            ApplyDownloadResponse(args, response);
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

    private ServiceDownloadRequestedEventArgs CreateDownloadRequest(
        CoreWebView2DownloadStartingEventArgs args)
    {
        var operation = args.DownloadOperation;
        var suggestedFileName = Path.GetFileName(args.ResultFilePath);
        return new ServiceDownloadRequestedEventArgs(
            ProfileName,
            WebViewRequestPolicy.CreateSafeOrigin(operation.Uri),
            string.IsNullOrWhiteSpace(suggestedFileName) ? "download" : suggestedFileName,
            operation.MimeType,
            operation.TotalBytesToReceive);
    }

    private void RequestDownloadDecision(ServiceDownloadRequestedEventArgs request)
    {
        if (DownloadRequested is null)
        {
            request.Cancel();
            return;
        }

        DownloadRequested.Invoke(this, request);
    }

    private void ApplyDownloadResponse(
        CoreWebView2DownloadStartingEventArgs args,
        ServiceDownloadResponse response)
    {
        args.Handled = true;
        if (response.Decision == ServiceDownloadDecision.Cancel ||
            string.IsNullOrWhiteSpace(response.DestinationPath))
        {
            args.Cancel = true;
            return;
        }

        args.ResultFilePath = response.DestinationPath;
        TrackDownload(args.DownloadOperation, response.DestinationPath);
    }

    private void TrackDownload(CoreWebView2DownloadOperation operation, string destinationPath)
    {
        var tracker = new WebViewDownloadTracker(
            ProfileName,
            operation,
            destinationPath,
            PublishDownloadState,
            completed => _downloads.Remove(completed));
        _downloads.Add(tracker);
        tracker.Attach();
    }

    private void PublishDownloadState(ServiceDownloadState state) =>
        DownloadStateChanged?.Invoke(this, new ServiceDownloadStateChangedEventArgs(state));

    private void OnNotificationReceived(
        CoreWebView2 sender,
        CoreWebView2NotificationReceivedEventArgs args)
    {
        if (NotificationReceived is null)
        {
            return;
        }

        var webNotification = args.Notification;
        args.Handled = true;
        webNotification.ReportShown();
        var notification = new ServiceNotification(
            ProfileName,
            WebViewRequestPolicy.CreateSafeOrigin(args.SenderOrigin),
            webNotification.Title ?? string.Empty,
            webNotification.Body ?? string.Empty,
            webNotification.Tag ?? string.Empty,
            webNotification.IsSilent,
            webNotification.RequiresInteraction);
        try
        {
            NotificationReceived.Invoke(
                this,
                new ServiceNotificationReceivedEventArgs(
                    notification,
                    webNotification.ReportClicked,
                    webNotification.ReportClosed));
        }
        catch
        {
            TryReportNotificationClosed(webNotification);
        }
    }

    private static void TryReportNotificationClosed(CoreWebView2Notification notification)
    {
        try
        {
            notification.ReportClosed();
        }
        catch
        {
            // The browser may close while the native handler is unwinding.
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
                FaviconChanged.Invoke(this, new ServiceFaviconChangedEventArgs(buffer.ToArray()));
            }
        }
        catch
        {
            // Initials remain visible when a page has no usable favicon.
        }
    }

    private static ServiceViewProcessFailureKind MapFailureKind(CoreWebView2ProcessFailedKind kind) => kind switch
    {
        CoreWebView2ProcessFailedKind.BrowserProcessExited => ServiceViewProcessFailureKind.BrowserExited,
        CoreWebView2ProcessFailedKind.RenderProcessExited => ServiceViewProcessFailureKind.RendererExited,
        CoreWebView2ProcessFailedKind.RenderProcessUnresponsive => ServiceViewProcessFailureKind.RendererUnresponsive,
        _ => ServiceViewProcessFailureKind.NonFatal
    };
}
