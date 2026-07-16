using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Catalog;
using Syltr.Config;
using Syltr.Config.Notifications;
using Syltr.Engine;
using Syltr.Engine.Spike;
using Syltr.Icon;
using Syltr.Localization;

namespace Syltr.Window;

public sealed partial class MainPage
{

    private async Task DeleteSelectedProfileAsync()
    {
        var railItem = SelectedRailItem();
        var host = railItem?.Host;
        if (railItem is null || host is null)
        {
            return;
        }

        var group = _railGroups.First(candidate => candidate.Items.Contains(railItem));
        var removingAccountFromGroup = group.Items.Count > 1;
        if (!await _dialogs.ConfirmRemovalAsync(railItem.Tile.Name, removingAccountFromGroup))
        {
            return;
        }

        var remainingServices = _services
            .Where(service => WebViewProfileName.FromServiceId(service.Id) != host.ProfileName)
            .ToArray();
        if (_serviceStore is not null)
        {
            await _serviceStore.SaveAsync(remainingServices);
        }

        _services.Clear();
        _services.AddRange(remainingServices);
        DetachHostEvents(host);
        _hosts.Remove(host);
        _states.Remove(host.ProfileName);
        RemoveRailItem(railItem);
        try
        {
            host.DeleteProfile();
        }
        catch
        {
            host.Dispose();
        }

        StatusInfoBar.Title = AppText.Get("RemoveService_CompletedTitle");
        StatusInfoBar.Message = AppText.Format("RemoveService_CompletedMessage", railItem.Tile.Name);
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        UpdateSelectedProfileStatus();
    }

    private void RemoveRailItem(ServiceRailItem railItem)
    {
        var group = _railGroups.First(group => group.Items.Contains(railItem));
        var removedGroupIndex = _railGroups.IndexOf(group);
        var groupWasSelected = ReferenceEquals(ServiceRail.SelectedItem, group);
        group.Items.Remove(railItem);
        _railItems.Remove(railItem);
        if (group.Items.Count == 0)
        {
            _railGroups.Remove(group);
        }
        else if (ReferenceEquals(group.ActiveItem, railItem))
        {
            group.ActiveItem = group.Items[0];
        }

        if (group.Items.Count > 0)
        {
            group.RefreshDisplayName();
            group.RefreshUnread();
        }

        if (groupWasSelected && group.Items.Count == 0)
        {
            ServiceRail.SelectedIndex = _railGroups.Count == 0
                ? -1
                : Math.Min(removedGroupIndex, _railGroups.Count - 1);
        }

        SelectRailContent();
        UpdateServiceOverlay();
    }

    private void OnPopupRequested(object? sender, ServicePopupRequestedEventArgs e)
    {
        if (e.RequestedUri is null)
        {
            e.Cancel();
            return;
        }

        e.PopupHost.PopupRequested += OnPopupRequested;
        e.PopupHost.PermissionRequested += OnPermissionRequested;
        e.PopupHost.DownloadRequested += OnDownloadRequested;
        e.PopupHost.DownloadStateChanged += OnDownloadStateChanged;
        e.PopupHost.NotificationReceived += OnNotificationReceived;
        e.PopupHost.ExternalNavigationRequested += OnExternalNavigationRequested;
        e.PopupHost.ConsoleMessageReceived += OnConsoleMessageReceived;
        var popupWindow = new ServicePopupWindow(e.PopupHost, e.RequestedUri);
        popupWindow.Closed += (_, _) =>
        {
            e.PopupHost.PopupRequested -= OnPopupRequested;
            e.PopupHost.PermissionRequested -= OnPermissionRequested;
            e.PopupHost.DownloadRequested -= OnDownloadRequested;
            e.PopupHost.DownloadStateChanged -= OnDownloadStateChanged;
            e.PopupHost.NotificationReceived -= OnNotificationReceived;
            e.PopupHost.ExternalNavigationRequested -= OnExternalNavigationRequested;
            e.PopupHost.ConsoleMessageReceived -= OnConsoleMessageReceived;
            _popupWindows.Remove(popupWindow);
        };
        _popupWindows.Add(popupWindow);
        popupWindow.Activate();
        e.OpenInApp();
    }

    private async void OnExternalNavigationRequested(
        object? sender,
        ServiceExternalNavigationRequestedEventArgs e)
    {
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(e.Destination))
            {
                StatusInfoBar.Title = AppText.Get("ExternalLink_FailedTitle");
                StatusInfoBar.Message = e.DestinationOrigin?.ToString() ?? e.Destination.Scheme;
                StatusInfoBar.Severity = InfoBarSeverity.Error;
            }
        }
        catch
        {
            StatusInfoBar.Title = AppText.Get("ExternalLink_FailedTitle");
            StatusInfoBar.Message = e.DestinationOrigin?.ToString() ?? e.Destination.Scheme;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
        }
    }

    private void OnConsoleMessageReceived(object? sender, ServiceConsoleMessageReceivedEventArgs e)
    {
        if (_webConsoleLog is not null)
        {
            _ = _webConsoleLog.AppendAsync(e.Message);
        }
    }

    private void OnNotificationReceived(object? sender, ServiceNotificationReceivedEventArgs e)
    {
        var isServiceMuted = _services.Any(service =>
            service.Muted &&
            WebViewProfileName.FromServiceId(service.Id) == e.Notification.ProfileName);
        if (_settings.DoNotDisturb || isServiceMuted)
        {
            e.Close();
            return;
        }

        var notification = e.Notification;
        if (TryShowWindowsNotification(notification, e))
        {
            return;
        }

        ShowInAppNotification(notification, e);
    }

    private bool TryShowWindowsNotification(
        ServiceNotification notification,
        ServiceNotificationReceivedEventArgs interaction)
    {
        if (_windowsNotifications is not { IsRegistered: true })
        {
            return false;
        }

        try
        {
            var notificationId = _windowsNotifications.Show(
                notification.ProfileName,
                string.IsNullOrWhiteSpace(notification.Title)
                    ? notification.SenderOrigin.Host
                    : notification.Title,
                notification.Body);
            _webNotifications[notificationId] = interaction;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ShowInAppNotification(
        ServiceNotification notification,
        ServiceNotificationReceivedEventArgs interaction)
    {
        _status.ShowNotification(notification, interaction);
    }

    private async void OnFaviconChanged(object? sender, ServiceFaviconChangedEventArgs e)
    {
        if (sender is not ServiceViewHost host)
        {
            return;
        }

        var railItem = _railItems.FirstOrDefault(item => ReferenceEquals(item.Host, host));
        if (railItem is null)
        {
            return;
        }

        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(e.PngBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            railItem.Tile.IconSource = bitmap;
            railItem.Tile.FaviconSize = Math.Clamp(
                Math.Max(bitmap.PixelWidth, bitmap.PixelHeight),
                16,
                24);
        }
        catch
        {
            // The service initial remains as the accessible fallback.
        }
    }

    private void OnWindowsNotificationActivated(
        object? sender,
        WindowsNotificationActivation activation)
    {
        DispatcherQueue.TryEnqueue(() => HandleWindowsNotificationActivation(activation));
    }

    private void HandleWindowsNotificationActivation(WindowsNotificationActivation activation)
    {
        ((App)Microsoft.UI.Xaml.Application.Current).ActivateMainWindow();
        if (!SelectProfile(activation.ProfileName))
        {
            _pendingActivatedProfile = activation.ProfileName;
        }

        if (_webNotifications.Remove(activation.NotificationId, out var webNotification))
        {
            webNotification.Click();
        }
    }

    private void OnStatusInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _status.OnClosed();
    }

    private async void OnDoNotDisturbChanged(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loadingSettings || _settingsStore is null)
        {
            return;
        }

        _settings = _settings with { DoNotDisturb = DoNotDisturbButton.IsChecked == true };
        DoNotDisturbIcon.Glyph = _settings.DoNotDisturb
            ? DoNotDisturbGlyph
            : NotificationsEnabledGlyph;
        await _settingsStore.SaveAsync(_settings);
        StatusInfoBar.Title = _settings.DoNotDisturb
            ? AppText.Get("DoNotDisturb_EnabledTitle")
            : AppText.Get("DoNotDisturb_DisabledTitle");
        StatusInfoBar.Message = _settings.DoNotDisturb
            ? AppText.Get("DoNotDisturb_EnabledMessage")
            : AppText.Get("DoNotDisturb_DisabledMessage");
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
    }

    private bool SelectProfile(string profileName)
    {
        var railItem = _railItems.FirstOrDefault(item => item.Host.ProfileName == profileName);
        if (railItem is not null)
        {
            SelectRailItem(railItem);
            return true;
        }

        return false;
    }

    private void OnDownloadRequested(object? sender, ServiceDownloadRequestedEventArgs request)
    {
        ShowDownloadStatus(_downloads.Start(request));
    }

    private void OnDownloadStateChanged(object? sender, ServiceDownloadStateChangedEventArgs e)
    {
        var presentation = _downloads.Update(e.State);
        ShowDownloadStatus(presentation);
        if (presentation.ShouldNotify)
        {
            TryShowDownloadCompletionNotification(e.State);
        }
    }

    private void ShowDownloadStatus(DownloadPresentation presentation)
    {
        StatusInfoBar.Title = presentation.Title;
        StatusInfoBar.Message = presentation.Message;
        StatusInfoBar.Severity = presentation.Severity;
    }

    private void TryShowDownloadCompletionNotification(ServiceDownloadState state)
    {
        if (_settings.DoNotDisturb || _windowsNotifications is not { IsRegistered: true })
        {
            return;
        }

        try
        {
            _windowsNotifications.Show(
                state.ProfileName,
                AppText.Get("Download_CompletedNotificationTitle"),
                state.FileName);
        }
        catch
        {
            // The in-app completion state remains visible as a fallback.
        }
    }

    private async void OnPermissionRequested(
        object? sender,
        ServicePermissionRequestedEventArgs request)
    {
        if (!_isPageLoaded)
        {
            request.Deny(rememberForProfile: false);
            return;
        }

        try
        {
            if (request.IsDecided || !_isPageLoaded)
            {
                return;
            }

            var decision = await _dialogs.RequestPermissionAsync(request);
            if (decision is null || !_isPageLoaded)
            {
                request.Deny(rememberForProfile: false);
                return;
            }

            if (decision.Allowed)
            {
                request.Allow(decision.RememberForProfile);
            }
            else
            {
                request.Deny(decision.RememberForProfile);
            }
        }
        catch
        {
            request.Deny(rememberForProfile: false);
        }
    }
}
