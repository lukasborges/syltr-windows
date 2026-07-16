using Microsoft.UI.Xaml.Controls;
using Syltr.Config;
using Syltr.Engine;
using Syltr.Engine.Spike;
using Syltr.Localization;

namespace Syltr.Window;

public sealed partial class MainPage
{
    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _isPageLoaded = true;
        ((App)Microsoft.UI.Xaml.Application.Current).SetMainWindowDragRegion(TitleDragRegion);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var paths = ApplicationDataPaths.ForCurrentUser();
        InitializeInfrastructure(paths);

        try
        {
            await LoadSettingsAsync();
            var loadStatus = await LoadServicesAsync();
            if (await HandleEmptyServiceListAsync(loadStatus))
            {
                return;
            }

            AddConfiguredServiceHosts();
            var initialization = await InitializeEnabledHostsAsync();
            await ReportIsolationDiagnosticsAsync(paths, initialization.ReadyHosts);
            ReportHostInitializationFailures(initialization);
            ApplyPendingNotificationActivations();
        }
        catch (Exception exception)
        {
            await ReportStartupFailureAsync(paths, exception);
        }
    }

    private void InitializeInfrastructure(ApplicationDataPaths paths)
    {
        _diagnosticsEnabled = Environment.GetEnvironmentVariable("SYLTR_DEBUG") == "1";
        _webConsoleLog = _diagnosticsEnabled ? new WebConsoleDiagnosticLog(paths) : null;
        _serviceStore = new ServiceConfigurationStore(paths);
        _settingsStore = new SettingsConfigurationStore(paths);
        _environment = new WebViewEnvironmentProvider(paths);
        _contentMapping = new ServiceViewContentMapping(
            "syltr.test",
            Path.Combine(AppContext.BaseDirectory, "Engine", "Spike"));

        _windowsNotifications = ((App)Microsoft.UI.Xaml.Application.Current).Notifications;
        _windowsNotifications.Activated += OnWindowsNotificationActivated;
    }

    private async Task LoadSettingsAsync()
    {
        _loadingSettings = true;
        try
        {
            _settings = (await _settingsStore!.LoadAsync()).Value;
            DoNotDisturbButton.IsChecked = _settings.DoNotDisturb;
            DoNotDisturbIcon.Glyph = _settings.DoNotDisturb
                ? DoNotDisturbGlyph
                : NotificationsEnabledGlyph;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private async Task<ConfigurationLoadStatus> LoadServicesAsync()
    {
        var result = await _serviceStore!.LoadAsync();
        _services.AddRange(result.Value);
        if (result.Status == ConfigurationLoadStatus.Created)
        {
            await _serviceStore.SaveAsync(_services);
        }

        return result.Status;
    }

    private async Task<bool> HandleEmptyServiceListAsync(ConfigurationLoadStatus loadStatus)
    {
        if (_services.Count > 0)
        {
            return false;
        }

        StatusInfoBar.Title = AppText.Get("Status_NoServicesTitle");
        StatusInfoBar.Message = AppText.Get("Status_NoServicesMessage");
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        UpdateServiceOverlay();
        if (loadStatus == ConfigurationLoadStatus.Created)
        {
            await Task.Yield();
            await ShowAddServiceDialogAsync();
        }

        return true;
    }

    private void AddConfiguredServiceHosts()
    {
        foreach (var service in _services)
        {
            AddServiceHost(service);
        }

        ServiceRail.SelectedIndex = 0;
        UpdateWebViewMemoryTargets();
    }

    private async Task<HostInitializationResult> InitializeEnabledHostsAsync()
    {
        var enabledHosts = _hosts
            .Where(host => host.State.Status != ServiceViewStatus.Disabled)
            .ToArray();
        var initialized = await Task.WhenAll(enabledHosts.Select(TryInitializeHostAsync));
        var readyHosts = enabledHosts
            .Where((_, index) => initialized[index])
            .ToArray();
        return new HostInitializationResult(enabledHosts, readyHosts);
    }

    private async Task ReportIsolationDiagnosticsAsync(
        ApplicationDataPaths paths,
        IReadOnlyCollection<ServiceViewHost> readyHosts)
    {
        var diagnosticProfiles = _services
            .Where(service => new Uri(service.Url).Host.Equals("syltr.test", StringComparison.OrdinalIgnoreCase))
            .Select(service => WebViewProfileName.FromServiceId(service.Id))
            .ToHashSet(StringComparer.Ordinal);
        if (diagnosticProfiles.Count == 0)
        {
            return;
        }

        var readyDiagnosticHosts = readyHosts
            .Where(host => diagnosticProfiles.Contains(host.ProfileName))
            .ToArray();
        if (readyDiagnosticHosts.Length < 2)
        {
            ReportInsufficientDiagnosticProfiles();
            return;
        }

        var probe = await ProfileIsolationProbe.RunAsync(readyDiagnosticHosts);
        ShowProbeResult(probe);
        if (probe.CurrentRunIsIsolated && readyDiagnosticHosts.Length == diagnosticProfiles.Count)
        {
            SpikeDiagnosticLog.Clear(paths);
        }
    }

    private void ReportInsufficientDiagnosticProfiles()
    {
        StatusInfoBar.Title = AppText.Get("Diagnostics_InsufficientProfilesTitle");
        StatusInfoBar.Message = AppText.Get("Diagnostics_InsufficientProfilesMessage");
        StatusInfoBar.Severity = InfoBarSeverity.Error;
    }

    private void ReportHostInitializationFailures(HostInitializationResult initialization)
    {
        if (initialization.ReadyHosts.Count == initialization.EnabledHosts.Count)
        {
            return;
        }

        StatusInfoBar.Title = AppText.Get("Status_SomeProfilesFailedTitle");
        StatusInfoBar.Message = AppText.Format(
            "Status_SomeProfilesFailedMessage",
            initialization.ReadyHosts.Count,
            initialization.EnabledHosts.Count);
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
    }

    private void ApplyPendingNotificationActivations()
    {
        UpdateSelectedProfileStatus();
        foreach (var activation in _windowsNotifications!.DrainPendingActivations())
        {
            HandleWindowsNotificationActivation(activation);
        }

        if (_pendingActivatedProfile is not null && SelectProfile(_pendingActivatedProfile))
        {
            _pendingActivatedProfile = null;
        }
    }

    private async Task ReportStartupFailureAsync(ApplicationDataPaths paths, Exception exception)
    {
        _loadingSettings = false;
        await SpikeDiagnosticLog.WriteFailureAsync(paths, exception);
        StatusInfoBar.Title = AppText.Get("Status_WebViewInitializationFailedTitle");
        StatusInfoBar.Message = AppText.Format(
            "Status_WebViewInitializationFailedMessage",
            exception.Message,
            SpikeDiagnosticLog.GetPath(paths));
        StatusInfoBar.Severity = InfoBarSeverity.Error;
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _isPageLoaded = false;
        _initialized = false;
        _status.Stop();
        UnsubscribeFromNotifications();
        CloseBrowserNotifications();
        DisposeServiceHosts();
        ClosePopupWindows();
        ClearViewState();
    }

    private void UnsubscribeFromNotifications()
    {
        if (_windowsNotifications is not null)
        {
            _windowsNotifications.Activated -= OnWindowsNotificationActivated;
        }
    }

    private void CloseBrowserNotifications()
    {
        foreach (var notification in _webNotifications.Values)
        {
            notification.Close();
        }

        _webNotifications.Clear();
    }

    private void DisposeServiceHosts()
    {
        foreach (var host in _hosts)
        {
            DetachHostEvents(host);
            host.Dispose();
        }
    }

    private void ClosePopupWindows()
    {
        foreach (var popupWindow in _popupWindows.ToArray())
        {
            popupWindow.Close();
        }
    }

    private void ClearViewState()
    {
        _hosts.Clear();
        _states.Clear();
        _services.Clear();
        _railItems.Clear();
        _railGroups.Clear();
        InstanceSelector.ItemsSource = null;
        ServiceContentPresenter.Content = null;
        _popupWindows.Clear();
        _downloads.Clear();
    }

    private sealed record HostInitializationResult(
        IReadOnlyList<ServiceViewHost> EnabledHosts,
        IReadOnlyList<ServiceViewHost> ReadyHosts);
}
