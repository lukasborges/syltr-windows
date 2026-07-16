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

    private ServiceViewHost? SelectedHost() =>
        SelectedRailItem()?.Host;

    private ServiceRailItem? SelectedRailItem() =>
        (ServiceRail.SelectedItem as ServiceRailGroupItem)?.ActiveItem;

    private void SelectRailItem(ServiceRailItem item)
    {
        var group = _railGroups.First(group => group.Items.Contains(item));
        group.ActiveItem = item;
        if (ReferenceEquals(ServiceRail.SelectedItem, group))
        {
            SelectRailContent();
        }
        else
        {
            ServiceRail.SelectedItem = group;
        }
    }

    private ServiceViewHost? SelectedReadyHost() =>
        SelectedHost() is { IsInitialized: true } host
            ? host
            : null;

    private void UpdateWebViewMemoryTargets()
    {
        var foregroundHost = SelectedHost();
        foreach (var host in _hosts)
        {
            host.SetForeground(ReferenceEquals(host, foregroundHost));
        }
    }

    private void ShowProbeResult(ProfileIsolationProbeResult result)
    {
        if (!result.CurrentRunIsIsolated)
        {
            StatusInfoBar.Title = AppText.Get("Diagnostics_IsolationFailedTitle");
            StatusInfoBar.Message = AppText.Get("Diagnostics_IsolationFailedMessage");
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        if (result.PersistenceWasDetected)
        {
            StatusInfoBar.Title = AppText.Get("Diagnostics_IsolationPersistenceTitle");
            StatusInfoBar.Message = AppText.Format(
                "Diagnostics_IsolationPersistenceMessage",
                result.Profiles.Count);
            return;
        }

        StatusInfoBar.Title = AppText.Get("Diagnostics_IsolationCurrentRunTitle");
        StatusInfoBar.Message = AppText.Format(
            "Diagnostics_IsolationCurrentRunMessage",
            result.Profiles.Count);
    }

    private void UpdateSelectedProfileStatus()
    {
        var host = SelectedHost();
        if (host is null || !_states.TryGetValue(host.ProfileName, out var state))
        {
            ServiceTitleText.Text = "Syltr";
            ProfileStatusText.Text = string.Empty;
            return;
        }

        ServiceTitleText.Text = SelectedRailItem()?.Tile.Name ?? "Syltr";
        var title = string.IsNullOrWhiteSpace(state.DocumentTitle)
            ? AppText.Get("ProfileStatus_Untitled")
            : state.DocumentTitle;
        ProfileStatusText.Text = AppText.Format(
            "ProfileStatus_Summary",
            host.ProfileName,
            LocalizeServiceViewStatus(state.Status),
            title,
            state.UnreadCount);
    }

    private static string LocalizeServiceViewStatus(ServiceViewStatus status) => status switch
    {
        ServiceViewStatus.Created => AppText.Get("ProfileStatus_Created"),
        ServiceViewStatus.Initializing => AppText.Get("ProfileStatus_Initializing"),
        ServiceViewStatus.Ready => AppText.Get("ProfileStatus_Ready"),
        ServiceViewStatus.Navigating => AppText.Get("ProfileStatus_Navigating"),
        ServiceViewStatus.Recovering => AppText.Get("ProfileStatus_Recovering"),
        ServiceViewStatus.Failed => AppText.Get("ProfileStatus_Failed"),
        ServiceViewStatus.Disabled => AppText.Get("ProfileStatus_Disabled"),
        ServiceViewStatus.Closed => AppText.Get("ProfileStatus_Closed"),
        _ => status.ToString()
    };

    private void UpdateServiceOverlay()
    {
        var host = SelectedHost();
        if (host is null)
        {
            ServiceStateOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ServiceStateProgressRing.IsActive = false;
            ServiceStateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ServiceStateTitle.Text = AppText.Get("State_NoServicesTitle");
            ServiceStateMessage.Text = AppText.Get("State_NoServicesMessage");
            ServiceStateActionButton.Content = AppText.Get("Header_AddService");
            ServiceStateActionButton.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            return;
        }

        var state = host.State;
        if (state.Status is ServiceViewStatus.Ready or ServiceViewStatus.Disabled)
        {
            ServiceStateOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ServiceStateProgressRing.IsActive = false;
            return;
        }

        ServiceStateOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        if (state.Status is ServiceViewStatus.Created or
            ServiceViewStatus.Initializing or
            ServiceViewStatus.Navigating or
            ServiceViewStatus.Recovering)
        {
            ServiceStateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ServiceStateProgressRing.IsActive = true;
            ServiceStateTitle.Text = state.Status == ServiceViewStatus.Recovering
                ? AppText.Get("State_Recovering")
                : AppText.Get("State_Loading");
            ServiceStateMessage.Text = AppText.Format("State_Profile", host.ProfileName);
            ServiceStateActionButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        ServiceStateProgressRing.IsActive = false;
        ServiceStateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ServiceStateTitle.Text = state.Status == ServiceViewStatus.Failed
            ? AppText.Get("State_LoadFailed")
            : AppText.Get("State_Unavailable");
        ServiceStateMessage.Text = state.ErrorMessage ?? AppText.Format("State_Profile", host.ProfileName);
        ServiceStateActionButton.Content = AppText.Get("State_TryAgain");
        ServiceStateActionButton.Visibility = state.Status == ServiceViewStatus.Failed
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void OnServiceStateActionClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (SelectedHost() is null)
        {
            await ShowAddServiceDialogAsync();
            return;
        }

        await RecoverSelectedHostAsync();
    }

    private static async Task<bool> TryInitializeHostAsync(ServiceViewHost host)
    {
        try
        {
            await host.InitializeAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private (ServiceViewHost Host, ServiceRailItem RailItem) AddServiceHost(ServiceDefinition service)
    {
        if (_environment is null || _contentMapping is null)
        {
            throw new InvalidOperationException("The WebView environment is not ready.");
        }

        var configuredHome = new Uri(service.Url);
        var home = configuredHome.Host.Equals("syltr.test", StringComparison.OrdinalIgnoreCase)
            ? new Uri($"https://syltr.test/isolation.html?profile={Uri.EscapeDataString(service.Id)}")
            : configuredHome;
        var host = new ServiceViewHost(
            _environment,
            service.Id,
            home,
            _contentMapping,
            service.UserAgent,
            _diagnosticsEnabled);
        AttachHostEvents(host);
        _hosts.Add(host);
        _states[host.ProfileName] = host.State;

        var railItem = new ServiceRailItem(
            service,
            host,
            service.Disabled ? CreateDisabledContent(service) : host.Content);
        railItem.Tile.IconSource = CreateBundledCatalogIcon(service);
        if (service.Disabled)
        {
            host.Suspend();
        }

        _railItems.Add(railItem);
        var groupKey = ServiceGroupKey.FromService(service);
        var group = _railGroups.FirstOrDefault(candidate => candidate.Key == groupKey);
        if (group is null)
        {
            group = new ServiceRailGroupItem(groupKey, railItem);
            _railGroups.Add(group);
        }
        else
        {
            group.Items.Add(railItem);
            group.RefreshDisplayName();
            group.RefreshUnread();
        }

        return (host, railItem);
    }

    private void AttachHostEvents(ServiceViewHost host)
    {
        host.StateChanged += OnHostStateChanged;
        host.PopupRequested += OnPopupRequested;
        host.PermissionRequested += OnPermissionRequested;
        host.DownloadRequested += OnDownloadRequested;
        host.DownloadStateChanged += OnDownloadStateChanged;
        host.NotificationReceived += OnNotificationReceived;
        host.FaviconChanged += OnFaviconChanged;
        host.ExternalNavigationRequested += OnExternalNavigationRequested;
        host.ConsoleMessageReceived += OnConsoleMessageReceived;
    }

    private void DetachHostEvents(ServiceViewHost host)
    {
        host.StateChanged -= OnHostStateChanged;
        host.PopupRequested -= OnPopupRequested;
        host.PermissionRequested -= OnPermissionRequested;
        host.DownloadRequested -= OnDownloadRequested;
        host.DownloadStateChanged -= OnDownloadStateChanged;
        host.NotificationReceived -= OnNotificationReceived;
        host.FaviconChanged -= OnFaviconChanged;
        host.ExternalNavigationRequested -= OnExternalNavigationRequested;
        host.ConsoleMessageReceived -= OnConsoleMessageReceived;
    }

    private static SvgImageSource? CreateBundledCatalogIcon(ServiceDefinition service)
    {
        if (!Uri.TryCreate(service.Url, UriKind.Absolute, out var serviceUri))
        {
            return null;
        }

        var entry = ServiceCatalog.Entries.FirstOrDefault(candidate =>
            new Uri(candidate.Url).Host.Equals(serviceUri.Host, StringComparison.OrdinalIgnoreCase));
        return entry is null
            ? null
            : CreateBundledCatalogIcon(entry);
    }

    private static SvgImageSource CreateBundledCatalogIcon(ServiceCatalogEntry entry) =>
        new(new Uri($"ms-appx:///Assets/ServiceIcons/{entry.Key}.svg"));

    private void RegroupRailItem(ServiceRailItem item, ServiceDefinition service)
    {
        var oldGroup = _railGroups.First(group => group.Items.Contains(item));
        var newKey = ServiceGroupKey.FromService(service);
        if (oldGroup.Key == newKey)
        {
            oldGroup.RefreshDisplayName();
            oldGroup.RefreshUnread();
            return;
        }

        oldGroup.Items.Remove(item);
        if (oldGroup.Items.Count == 0)
        {
            _railGroups.Remove(oldGroup);
        }
        else
        {
            if (ReferenceEquals(oldGroup.ActiveItem, item))
            {
                oldGroup.ActiveItem = oldGroup.Items[0];
            }

            oldGroup.RefreshDisplayName();
        }

        var newGroup = _railGroups.FirstOrDefault(group => group.Key == newKey);
        if (newGroup is null)
        {
            newGroup = new ServiceRailGroupItem(newKey, item);
            _railGroups.Add(newGroup);
        }
        else
        {
            newGroup.Items.Add(item);
            newGroup.ActiveItem = item;
            newGroup.RefreshDisplayName();
            newGroup.RefreshUnread();
        }

        SelectRailItem(item);
    }

    private static Microsoft.UI.Xaml.FrameworkElement CreateDisabledContent(ServiceDefinition service)
    {
        var content = new StackPanel
        {
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Spacing = 10
        };
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE74F",
            FontSize = 40
        });
        content.Children.Add(new TextBlock
        {
            Text = AppText.Format("State_DisabledTitle", service.Name),
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = AppText.Get("State_DisabledMessage"),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        });
        return content;
    }
}
