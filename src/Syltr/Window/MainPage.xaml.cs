using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Catalog;
using Syltr.Config;
using Syltr.Config.Downloads;
using Syltr.Config.Notifications;
using Syltr.Engine;
using Syltr.Engine.Spike;
using Syltr.Icon;
using Syltr.Localization;
using Syltr.Spellcheck;

namespace Syltr.Window;

/// <summary>
/// Displays the main application content.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly List<ServiceViewHost> _hosts = [];
    private readonly ObservableCollection<ServiceRailItem> _railItems = [];
    private readonly ObservableCollection<ServiceRailGroupItem> _railGroups = [];
    private readonly Dictionary<string, ServiceViewState> _states = [];
    private readonly List<ServicePopupWindow> _popupWindows = [];
    private readonly List<ServiceDefinition> _services = [];
    private readonly SemaphoreSlim _contentDialogGate = new(1, 1);
    private readonly Dictionary<Guid, ServiceNotificationReceivedEventArgs> _webNotifications = [];
    private readonly HashSet<string> _reservedDownloadPaths = new(StringComparer.OrdinalIgnoreCase);
    private AddServiceWindow? _addServiceWindow;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _statusDismissTimer;
    private ServiceConfigurationStore? _serviceStore;
    private SettingsConfigurationStore? _settingsStore;
    private ApplicationSettings _settings = new();
    private WebViewEnvironmentProvider? _environment;
    private ServiceViewContentMapping? _contentMapping;
    private WebConsoleDiagnosticLog? _webConsoleLog;
    private WindowsAppNotificationService? _windowsNotifications;
    private ServiceNotificationReceivedEventArgs? _activeNotification;
    private string? _pendingActivatedProfile;
    private bool _initialized;
    private bool _isPageLoaded;
    private bool _loadingSettings;
    private bool _changingInstanceSelection;
    private bool _diagnosticsEnabled;

    public MainPage()
    {
        InitializeComponent();
        ApplyLocalizedShellText();
        ServiceRail.ItemsSource = _railGroups;
        StatusInfoBar.RegisterPropertyChangedCallback(InfoBar.TitleProperty, OnStatusChanged);
        StatusInfoBar.RegisterPropertyChangedCallback(InfoBar.SeverityProperty, OnStatusChanged);
    }

    private void ApplyLocalizedShellText()
    {
        LocalizeHeaderButton(MainMenuButton, "Header_MainMenu", "Header_MainMenu_Tooltip");
        LocalizeHeaderButton(AddServiceButton, "Header_AddService", "Header_AddService_Tooltip");
        LocalizeHeaderButton(BackButton, "Header_Back", "Header_Back_Tooltip");
        LocalizeHeaderButton(ForwardButton, "Header_Forward", "Header_Forward_Tooltip");
        LocalizeHeaderButton(ReloadButton, "Header_Reload", "Header_Reload_Tooltip");
        LocalizeHeaderButton(HomeButton, "Header_Home", "Header_Home_Tooltip");
        LocalizeHeaderButton(DoNotDisturbButton, "Header_DoNotDisturb", "Header_DoNotDisturb_Tooltip");
        AutomationProperties.SetName(ServiceRail, AppText.Get("ServiceRail_AutomationName"));
        AutomationProperties.SetHelpText(ServiceRail, AppText.Get("ServiceRail_HelpText"));
        AutomationProperties.SetName(InstanceSelector, AppText.Get("InstanceSelector_AutomationName"));
        LocalDiagnosticTargetItem.Content = AppText.Get("Diagnostics_LocalTarget");
        StatusInfoBar.Message = AppText.Get("Status_InitializingServices");
    }

    private static void LocalizeHeaderButton(
        Microsoft.UI.Xaml.DependencyObject button,
        string automationResource,
        string tooltipResource)
    {
        AutomationProperties.SetName(button, AppText.Get(automationResource));
        ToolTipService.SetToolTip(button, AppText.Get(tooltipResource));
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _isPageLoaded = true;
        ((App)Microsoft.UI.Xaml.Application.Current).SetMainWindowDragRegion(TitleDragRegion);
        _diagnosticsEnabled = Environment.GetEnvironmentVariable("SYLTR_DEBUG") == "1";
        _windowsNotifications = ((App)Microsoft.UI.Xaml.Application.Current).Notifications;
        _windowsNotifications.Activated += OnWindowsNotificationActivated;
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var paths = ApplicationDataPaths.ForCurrentUser();
        _webConsoleLog = _diagnosticsEnabled ? new WebConsoleDiagnosticLog(paths) : null;
        _serviceStore = new ServiceConfigurationStore(paths);
        _settingsStore = new SettingsConfigurationStore(paths);
        _environment = new WebViewEnvironmentProvider(paths);
        var contentFolder = Path.Combine(AppContext.BaseDirectory, "Engine", "Spike");
        _contentMapping = new ServiceViewContentMapping("syltr.test", contentFolder);

        try
        {
            _loadingSettings = true;
            var loadedSettings = await _settingsStore.LoadAsync();
            _settings = loadedSettings.Value;
            DoNotDisturbButton.IsChecked = _settings.DoNotDisturb;
            DoNotDisturbIcon.Glyph = _settings.DoNotDisturb ? "\uEB71" : "\uEA8F";
            _loadingSettings = false;

            var loaded = await _serviceStore.LoadAsync();
            _services.AddRange(loaded.Value);
            if (loaded.Status == ConfigurationLoadStatus.Created)
            {
                await _serviceStore.SaveAsync(_services);
            }

            if (_services.Count == 0)
            {
                StatusInfoBar.Title = AppText.Get("Status_NoServicesTitle");
                StatusInfoBar.Message = AppText.Get("Status_NoServicesMessage");
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                UpdateServiceOverlay();
                if (loaded.Status == ConfigurationLoadStatus.Created)
                {
                    await Task.Yield();
                    await ShowAddServiceDialogAsync();
                }
                return;
            }

            foreach (var service in _services)
            {
                AddServiceHost(service);
            }

            ServiceRail.SelectedIndex = 0;
            var enabledHosts = _hosts
                .Where(host => host.State.Status != ServiceViewStatus.Disabled)
                .ToArray();
            var initialization = await Task.WhenAll(enabledHosts.Select(TryInitializeHostAsync));
            var readyHosts = enabledHosts
                .Where((_, index) => initialization[index])
                .ToArray();
            var diagnosticProfiles = _services
                .Where(service => new Uri(service.Url).Host.Equals("syltr.test", StringComparison.OrdinalIgnoreCase))
                .Select(service => WebViewProfileName.FromServiceId(service.Id))
                .ToHashSet(StringComparer.Ordinal);
            var readyDiagnosticHosts = readyHosts
                .Where(host => diagnosticProfiles.Contains(host.ProfileName))
                .ToArray();
            if (readyDiagnosticHosts.Length >= 2)
            {
                var probe = await ProfileIsolationProbe.RunAsync(readyDiagnosticHosts);
                ShowProbeResult(probe);
                if (probe.CurrentRunIsIsolated && readyDiagnosticHosts.Length == diagnosticProfiles.Count)
                {
                    SpikeDiagnosticLog.Clear(paths);
                }
            }
            else if (diagnosticProfiles.Count > 0)
            {
                StatusInfoBar.Title = AppText.Get("Diagnostics_InsufficientProfilesTitle");
                StatusInfoBar.Message = AppText.Get("Diagnostics_InsufficientProfilesMessage");
                StatusInfoBar.Severity = InfoBarSeverity.Error;
            }

            if (readyHosts.Length != enabledHosts.Length)
            {
                StatusInfoBar.Title = AppText.Get("Status_SomeProfilesFailedTitle");
                StatusInfoBar.Message = AppText.Format(
                    "Status_SomeProfilesFailedMessage",
                    readyHosts.Length,
                    enabledHosts.Length);
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
            }

            UpdateSelectedProfileStatus();
            foreach (var activation in _windowsNotifications.DrainPendingActivations())
            {
                HandleWindowsNotificationActivation(activation);
            }

            if (_pendingActivatedProfile is not null && SelectProfile(_pendingActivatedProfile))
            {
                _pendingActivatedProfile = null;
            }
        }
        catch (Exception exception)
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
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _isPageLoaded = false;
        _initialized = false;
        _statusDismissTimer?.Stop();
        if (_windowsNotifications is not null)
        {
            _windowsNotifications.Activated -= OnWindowsNotificationActivated;
        }

        // Report browser notifications closed while their originating WebViews are still alive.
        _activeNotification?.Close();
        _activeNotification = null;
        foreach (var notification in _webNotifications.Values)
        {
            notification.Close();
        }

        _webNotifications.Clear();
        foreach (var host in _hosts)
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
            host.Dispose();
        }

        foreach (var popupWindow in _popupWindows.ToArray())
        {
            popupWindow.Close();
        }

        _hosts.Clear();
        _states.Clear();
        _services.Clear();
        _railItems.Clear();
        _railGroups.Clear();
        InstanceSelector.ItemsSource = null;
        ServiceContentPresenter.Content = null;
        _popupWindows.Clear();
        _reservedDownloadPaths.Clear();
    }

    private void OnHostStateChanged(object? sender, ServiceViewStateChangedEventArgs e)
    {
        if (sender is ServiceViewHost host)
        {
            _states[host.ProfileName] = e.State;
            var railItem = _railItems.FirstOrDefault(item => ReferenceEquals(item.Host, host));
            railItem?.UpdateState(e.State);
            if (railItem is not null)
            {
                _railGroups.First(group => group.Items.Contains(railItem)).RefreshUnread();
            }
            UpdateSelectedProfileStatus();
            if (ReferenceEquals(SelectedHost(), host))
            {
                UpdateServiceOverlay();
            }
        }
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectRailContent();

    private void OnRailItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ServiceRailGroupItem { Items.Count: > 1 } group ||
            ServiceRail.ContainerFromItem(group) is not Microsoft.UI.Xaml.FrameworkElement anchor)
        {
            return;
        }

        var flyout = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop
        };
        foreach (var instance in group.Items)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = instance.UnreadCount > 0
                    ? $"{instance.Tile.Name}  ({instance.UnreadCount})"
                    : instance.Tile.Name,
                IsChecked = ReferenceEquals(instance, group.ActiveItem)
            };
            item.Click += (_, _) => SelectRailItem(instance);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var addInstance = new MenuFlyoutItem { Text = AppText.Get("Context_AddInstance") };
        addInstance.Click += async (_, _) =>
        {
            var service = _services.FirstOrDefault(configured =>
                WebViewProfileName.FromServiceId(configured.Id) == group.ActiveItem.Host.ProfileName);
            if (service is not null)
            {
                await AddServiceAsync(service.Name, service.Url, promptForDuplicateName: true);
            }
        };
        flyout.Items.Add(addInstance);
        flyout.ShowAt(anchor);
    }

    private void OnServiceRailRightTapped(
        object sender,
        Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        var current = e.OriginalSource as Microsoft.UI.Xaml.DependencyObject;
        while (current is not null and not ListViewItem)
        {
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        if (current is not ListViewItem container ||
            ServiceRail.ItemFromContainer(container) is not ServiceRailGroupItem group)
        {
            return;
        }

        ServiceRail.SelectedItem = group;
        SelectRailContent();
        ShowServiceContextMenu(group, container);
        e.Handled = true;
    }

    private void OnServiceContextMenuAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ServiceRail.SelectedItem is ServiceRailGroupItem group
            && ServiceRail.ContainerFromItem(group) is ListViewItem container)
        {
            ShowServiceContextMenu(group, container);
            args.Handled = true;
        }
    }

    private void ShowServiceContextMenu(ServiceRailGroupItem group, ListViewItem container)
    {
        var railItem = group.ActiveItem;
        var service = _services.FirstOrDefault(configured =>
            WebViewProfileName.FromServiceId(configured.Id) == railItem.Host.ProfileName);
        if (service is null)
        {
            return;
        }

        var menu = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop
        };
        var reload = new MenuFlyoutItem { Text = AppText.Get("Context_Reload") };
        reload.Click += async (_, _) =>
        {
            if (SelectedReadyHost() is { } readyHost)
            {
                readyHost.Reload();
            }
            else
            {
                await RecoverSelectedHostAsync();
            }
        };
        var home = new MenuFlyoutItem { Text = AppText.Get("Context_Home") };
        home.Click += (_, _) => SelectedReadyHost()?.NavigateHome();
        var edit = new MenuFlyoutItem { Text = AppText.Get("Context_Edit") };
        edit.Click += async (_, _) => await ShowEditServiceDialogAsync();
        var mute = new MenuFlyoutItem
        {
            Text = AppText.Get(service.Muted ? "Context_Unmute" : "Context_Mute")
        };
        mute.Click += async (_, _) => await SetSelectedServiceMutedAsync(!service.Muted);
        var disable = new MenuFlyoutItem
        {
            Text = AppText.Get(service.Disabled ? "Context_Enable" : "Context_Disable")
        };
        disable.Click += async (_, _) => await SetSelectedServiceDisabledAsync(!service.Disabled);
        var remove = new MenuFlyoutItem
        {
            Text = AppText.Get(group.Items.Count > 1 ? "Context_RemoveAccount" : "Context_Remove")
        };
        remove.Click += async (_, _) => await DeleteSelectedProfileAsync();

        menu.Items.Add(reload);
        menu.Items.Add(home);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(edit);
        menu.Items.Add(mute);
        menu.Items.Add(disable);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        menu.ShowAt(container);
    }

    private async Task SetSelectedServiceMutedAsync(bool muted)
    {
        var railItem = SelectedRailItem();
        if (railItem is null || _serviceStore is null)
        {
            return;
        }

        var index = _services.FindIndex(service =>
            WebViewProfileName.FromServiceId(service.Id) == railItem.Host.ProfileName);
        if (index < 0)
        {
            return;
        }

        var updated = _services[index] with { Muted = muted };
        _services[index] = updated;
        railItem.UpdateService(updated);
        await _serviceStore.SaveAsync(_services);
        StatusInfoBar.Title = AppText.Get(muted
            ? "Notifications_MutedTitle"
            : "Notifications_UnmutedTitle");
        StatusInfoBar.Message = updated.Name;
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private async Task SetSelectedServiceDisabledAsync(bool disabled)
    {
        var railItem = SelectedRailItem();
        if (railItem is null || _serviceStore is null)
        {
            return;
        }

        var host = railItem.Host;
        var index = _services.FindIndex(service =>
            WebViewProfileName.FromServiceId(service.Id) == host.ProfileName);
        if (index < 0)
        {
            return;
        }

        var updated = _services[index] with { Disabled = disabled };
        _services[index] = updated;
        railItem.UpdateService(updated);
        await _serviceStore.SaveAsync(_services);
        if (disabled)
        {
            host.Suspend();
            railItem.Content = CreateDisabledContent(updated);
        }
        else
        {
            railItem.Content = host.Content;
            await host.ResumeAsync();
        }

        SelectRailContent();
        StatusInfoBar.Title = AppText.Get(disabled
            ? "Service_DisabledTitle"
            : "Service_EnabledTitle");
        StatusInfoBar.Message = disabled
            ? AppText.Get("Service_DisabledSessionPreserved")
            : updated.Name;
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private void SelectRailContent()
    {
        var group = ServiceRail.SelectedItem as ServiceRailGroupItem;
        _changingInstanceSelection = true;
        InstanceSelector.ItemsSource = group?.Items;
        InstanceSelector.SelectedItem = group?.ActiveItem;
        InstanceSelector.Visibility = group is { Items.Count: > 1 }
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        _changingInstanceSelection = false;
        ServiceContentPresenter.Content = group?.ActiveItem.Content;
        UpdateSelectedProfileStatus();
        UpdateServiceOverlay();
    }

    private void OnInstanceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingInstanceSelection ||
            ServiceRail.SelectedItem is not ServiceRailGroupItem group ||
            InstanceSelector.SelectedItem is not ServiceRailItem item)
        {
            return;
        }

        group.ActiveItem = item;
        ServiceContentPresenter.Content = item.Content;
        UpdateSelectedProfileStatus();
        UpdateServiceOverlay();
    }

    private async void OnRailDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_serviceStore is null)
        {
            return;
        }

        var servicesByProfile = _services.ToDictionary(
            service => WebViewProfileName.FromServiceId(service.Id),
            StringComparer.Ordinal);
        var reordered = _railGroups
            .SelectMany(group => group.Items)
            .Select(item => servicesByProfile[item.Host.ProfileName])
            .ToArray();
        if (reordered.Length != _services.Count)
        {
            return;
        }

        _services.Clear();
        _services.AddRange(reordered);
        await _serviceStore.SaveAsync(_services);
    }

    private void OnBackClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.GoBack();

    private void OnForwardClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.GoForward();

    private void OnHomeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.NavigateHome();

    private void OnReloadClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SelectedReadyHost()?.Reload();

    private async void OnAddServiceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowAddServiceDialogAsync();

    private async void OnAboutClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var version = typeof(MainPage).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var content = new StackPanel
        {
            MaxWidth = 420,
            Spacing = 10,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
        };
        content.Children.Add(new Image
        {
            Width = 96,
            Height = 96,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            Source = new SvgImageSource(new Uri("ms-appx:///Assets/Syltr.svg"))
        });
        content.Children.Add(new TextBlock
        {
            Text = "Syltr",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = AppText.Format("About_Version", version),
            Opacity = 0.7,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = AppText.Get("About_Developer"),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = AppText.Get("About_Description"),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 2),
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });

        var links = new StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            Spacing = 4
        };
        links.Children.Add(new HyperlinkButton
        {
            Content = AppText.Get("About_Website"),
            NavigateUri = new Uri("https://github.com/lukasborges/syltr")
        });
        links.Children.Add(new HyperlinkButton
        {
            Content = AppText.Get("About_ReportIssue"),
            NavigateUri = new Uri("https://github.com/lukasborges/syltr/issues")
        });
        content.Children.Add(links);
        content.Children.Add(new TextBlock
        {
            Text = AppText.Get("About_License"),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 0),
            FontSize = 12,
            Opacity = 0.7,
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppText.Get("About_Title"),
            Content = content,
            CloseButtonText = AppText.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private void OnExitClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ((App)Microsoft.UI.Xaml.Application.Current).CloseMainWindow();

    private async void OnSpellcheckClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var languages = WindowsSpellcheckPreferences.GetPreferredLanguages();
        var languageSummary = languages.Count == 0
            ? AppText.Get("Spellcheck_NoPreferredLanguages")
            : string.Join(
                Environment.NewLine,
                languages.Select(language => $"• {language.DisplayName} ({language.Id})"));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppText.Get("Spellcheck_Title"),
            Content = new TextBlock
            {
                Text = AppText.Format("Spellcheck_Description", languageSummary),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                MaxWidth = 460
            },
            PrimaryButtonText = AppText.Get("Spellcheck_OpenSettings"),
            CloseButtonText = AppText.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Windows.System.Launcher.LaunchUriAsync(WindowsSpellcheckPreferences.SettingsUri);
        }
    }

    private async Task ShowAddServiceDialogAsync()
    {
        if (_environment is null || _contentMapping is null || _serviceStore is null || XamlRoot is null)
        {
            return;
        }

        var owner = ((App)Microsoft.UI.Xaml.Application.Current).MainWindowInstance;
        if (owner is null)
        {
            return;
        }

        _addServiceWindow = new AddServiceWindow(owner);
        AddServiceWindowResult? result;
        try
        {
            result = await _addServiceWindow.ShowAsync();
        }
        finally
        {
            _addServiceWindow = null;
        }

        if (result?.Entry is not null)
        {
            await AddServiceAsync(result.Entry.Name, result.Entry.Url, promptForDuplicateName: true);
        }
        else if (result?.CustomRequested == true)
        {
            await ShowCustomServiceDialogAsync();
        }
    }


    private async Task ShowCustomServiceDialogAsync()
    {
        if (XamlRoot is null)
        {
            return;
        }

        var nameBox = new TextBox
        {
            Header = AppText.Get("Field_Name"),
            PlaceholderText = AppText.Get("CustomService_NamePlaceholder")
        };
        var urlBox = new TextBox { Header = AppText.Get("Field_Address"), PlaceholderText = "https://..." };
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(new TextBlock
        {
            Text = AppText.Get("CustomService_Description"),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });
        content.Children.Add(nameBox);
        content.Children.Add(urlBox);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppText.Get("CustomService_Title"),
            Content = content,
            PrimaryButtonText = AppText.Get("Common_Add"),
            CloseButtonText = AppText.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await AddServiceAsync(nameBox.Text, urlBox.Text, promptForDuplicateName: false);
        }
    }

    private async Task AddServiceAsync(string requestedName, string requestedUrl, bool promptForDuplicateName)
    {
        if (_serviceStore is null || XamlRoot is null ||
            !ServiceUrl.TryNormalize(requestedUrl, out var normalizedUrl))
        {
            StatusInfoBar.Title = AppText.Get("Service_InvalidTitle");
            StatusInfoBar.Message = AppText.Get("Service_InvalidNameAddress");
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        var name = requestedName.Trim();
        if (promptForDuplicateName && _services.Any(service => service.Url == normalizedUrl))
        {
            var nameBox = new TextBox { Header = AppText.Get("Field_Name"), Text = name };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = AppText.Get("Instance_NameTitle"),
                Content = nameBox,
                PrimaryButtonText = AppText.Get("Common_Add"),
                CloseButtonText = AppText.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            name = nameBox.Text.Trim();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            StatusInfoBar.Title = AppText.Get("Service_InvalidTitle");
            StatusInfoBar.Message = AppText.Get("Service_InvalidName");
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        var service = new ServiceDefinition
        {
            Id = ServiceIdGenerator.CreateUnique(_services, name),
            Name = name,
            Url = normalizedUrl
        };
        _services.Add(service);
        await _serviceStore.SaveAsync(_services);
        var (host, railItem) = AddServiceHost(service);
        SelectRailItem(railItem);
        if (await TryInitializeHostAsync(host))
        {
            StatusInfoBar.Title = AppText.Format("Service_AddedTitle", service.Name);
            StatusInfoBar.Message = AppText.Format("Service_AddedMessage", host.ProfileName);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        else
        {
            StatusInfoBar.Title = AppText.Format("Service_SavedNotLoadedTitle", service.Name);
            StatusInfoBar.Message = host.State.ErrorMessage ?? AppText.Get("Service_TryContextMenu");
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
        }
    }

    private async void OnEditServiceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await ShowEditServiceDialogAsync();

    private async Task ShowEditServiceDialogAsync()
    {
        var railItem = SelectedRailItem();
        var host = railItem?.Host;
        if (railItem is null || host is null || _serviceStore is null || XamlRoot is null)
        {
            return;
        }

        var serviceIndex = _services.FindIndex(service =>
            WebViewProfileName.FromServiceId(service.Id) == host.ProfileName);
        if (serviceIndex < 0)
        {
            return;
        }

        var service = _services[serviceIndex];
        var nameBox = new TextBox { Header = AppText.Get("Field_Name"), Text = service.Name };
        var urlBox = new TextBox { Header = AppText.Get("Field_Address"), Text = service.Url };
        var userAgentBox = new TextBox
        {
            Header = AppText.Get("EditService_UserAgent"),
            Text = service.UserAgent ?? string.Empty,
            PlaceholderText = AppText.Get("EditService_UserAgentPlaceholder")
        };
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(nameBox);
        content.Children.Add(urlBox);
        content.Children.Add(userAgentBox);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppText.Format("EditService_Title", service.Name),
            Content = content,
            PrimaryButtonText = AppText.Get("Common_Save"),
            CloseButtonText = AppText.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !ServiceUrl.TryNormalize(urlBox.Text, out var normalizedUrl))
        {
            StatusInfoBar.Title = AppText.Get("Service_InvalidTitle");
            StatusInfoBar.Message = AppText.Get("Service_InvalidNameAddress");
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        var updated = service with
        {
            Name = name,
            Url = normalizedUrl,
            UserAgent = string.IsNullOrWhiteSpace(userAgentBox.Text)
                ? null
                : userAgentBox.Text.Trim()
        };
        _services[serviceIndex] = updated;
        await _serviceStore.SaveAsync(_services);
        railItem.UpdateService(updated);
        RegroupRailItem(railItem, updated);
        host.UpdateHome(new Uri(updated.Url), navigate: false);
        host.SetUserAgent(updated.UserAgent, reload: false);
        if (updated.Disabled)
        {
            host.Suspend();
            railItem.Content = CreateDisabledContent(updated);
            ServiceContentPresenter.Content = railItem.Content;
        }
        else if (service.Disabled)
        {
            railItem.Content = host.Content;
            ServiceContentPresenter.Content = railItem.Content;
            await host.ResumeAsync();
        }
        else if (host.IsInitialized)
        {
            host.NavigateHome();
        }

        StatusInfoBar.Title = AppText.Format("Service_UpdatedTitle", updated.Name);
        StatusInfoBar.Message = updated.Disabled
            ? AppText.Format("Service_UpdatedDisabledMessage", updated.Name)
            : AppText.Format("Service_UpdatedSessionPreservedMessage", updated.Name);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private async Task RecoverSelectedHostAsync()
    {
        var host = SelectedHost();
        if (host is null || host.State.Status != ServiceViewStatus.Failed)
        {
            StatusInfoBar.Title = AppText.Get("Recovery_NotRequiredTitle");
            StatusInfoBar.Message = AppText.Get("Recovery_NotRequiredMessage");
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            return;
        }

        try
        {
            await host.RecoverAsync();
            StatusInfoBar.Title = AppText.Get("Recovery_CompletedTitle");
            StatusInfoBar.Message = AppText.Format("Recovery_CompletedMessage", host.ProfileName);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        catch (Exception exception)
        {
            StatusInfoBar.Title = AppText.Get("Recovery_FailedTitle");
            StatusInfoBar.Message = exception.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
        }
    }

    private async void OnAddServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ShowAddServiceDialogAsync();
    }

    private async void OnEditServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ShowEditServiceDialogAsync();
    }

    private void OnReloadAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.Reload();
    }

    private void OnHomeAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.NavigateHome();
    }

    private void OnBackAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.GoBack();
    }

    private void OnForwardAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectedReadyHost()?.GoForward();
    }

    private void OnNextServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectRelativeService(1);
    }

    private void OnPreviousServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectRelativeService(-1);
    }

    private void OnDoNotDisturbAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        DoNotDisturbButton.IsChecked = DoNotDisturbButton.IsChecked != true;
    }

    private void OnGotoServiceAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        var index = (int)sender.Key - (int)Windows.System.VirtualKey.Number1;
        if (index >= 0 && index < _railGroups.Count)
        {
            ServiceRail.SelectedIndex = index;
            args.Handled = true;
        }
    }

    private void OnQuitAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ((App)Microsoft.UI.Xaml.Application.Current).CloseMainWindow();
    }

    private void SelectRelativeService(int offset)
    {
        if (_railGroups.Count == 0)
        {
            return;
        }

        var currentIndex = Math.Max(0, ServiceRail.SelectedIndex);
        ServiceRail.SelectedIndex = (currentIndex + offset + _railGroups.Count) % _railGroups.Count;
    }

    private void OnNavigateAllClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var target = (TargetSelector.SelectedItem as ComboBoxItem)?.Tag as string;
        if (target == "local")
        {
            foreach (var host in _hosts.Where(host => host.IsInitialized))
            {
                host.NavigateHome();
            }
        }
        else if (Uri.TryCreate(target, UriKind.Absolute, out var destination))
        {
            foreach (var host in _hosts.Where(host => host.IsInitialized))
            {
                host.Navigate(destination);
            }
        }

        StatusInfoBar.Title = AppText.Get("Diagnostics_AuthenticationTitle");
        StatusInfoBar.Message = AppText.Get("Diagnostics_AuthenticationMessage");
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
    }

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
        var removeActionKey = removingAccountFromGroup ? "Context_RemoveAccount" : "Context_Remove";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppText.Format("RemoveService_Title", railItem.Tile.Name),
            Content = AppText.Get(removingAccountFromGroup
                ? "RemoveService_AccountDescription"
                : "RemoveService_Description"),
            PrimaryButtonText = AppText.Get(removeActionKey),
            CloseButtonText = AppText.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
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
        host.StateChanged -= OnHostStateChanged;
        host.PopupRequested -= OnPopupRequested;
        host.PermissionRequested -= OnPermissionRequested;
        host.DownloadRequested -= OnDownloadRequested;
        host.DownloadStateChanged -= OnDownloadStateChanged;
        host.NotificationReceived -= OnNotificationReceived;
        host.FaviconChanged -= OnFaviconChanged;
        host.ExternalNavigationRequested -= OnExternalNavigationRequested;
        host.ConsoleMessageReceived -= OnConsoleMessageReceived;
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

        _activeNotification?.Close();
        _activeNotification = e;
        var notification = e.Notification;
        if (_windowsNotifications is { IsRegistered: true })
        {
            try
            {
                var notificationId = _windowsNotifications.Show(
                    notification.ProfileName,
                    string.IsNullOrWhiteSpace(notification.Title)
                        ? notification.SenderOrigin.Host
                        : notification.Title,
                    notification.Body);
                _webNotifications[notificationId] = e;
            }
            catch
            {
                // The in-app notification below remains available as a fallback.
            }
        }

        var openButton = new Button { Content = AppText.Get("Notification_OpenService") };
        openButton.Click += (_, _) =>
        {
            SelectProfile(notification.ProfileName);
            e.Click();
            if (ReferenceEquals(_activeNotification, e))
            {
                _activeNotification = null;
                StatusInfoBar.ActionButton = null;
                StatusInfoBar.IsClosable = false;
            }
        };

        StatusInfoBar.Title = string.IsNullOrWhiteSpace(notification.Title)
            ? notification.SenderOrigin.Host
            : notification.Title;
        StatusInfoBar.Message = string.IsNullOrWhiteSpace(notification.Body)
            ? AppText.Format(
                "Notification_FallbackMessage",
                notification.SenderOrigin.Host,
                notification.ProfileName)
            : AppText.Format("Notification_BodyWithProfile", notification.Body, notification.ProfileName);
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.ActionButton = openButton;
        StatusInfoBar.IsClosable = true;
        StatusInfoBar.IsOpen = true;
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
        _statusDismissTimer?.Stop();
        _activeNotification?.Close();
        _activeNotification = null;
        sender.ActionButton = null;
        sender.IsClosable = true;
    }

    private void OnStatusChanged(
        Microsoft.UI.Xaml.DependencyObject sender,
        Microsoft.UI.Xaml.DependencyProperty property)
    {
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.IsClosable = true;
        if (_activeNotification is not null || StatusInfoBar.Severity == InfoBarSeverity.Error)
        {
            _statusDismissTimer?.Stop();
            return;
        }

        _statusDismissTimer ??= DispatcherQueue.CreateTimer();
        _statusDismissTimer.Stop();
        _statusDismissTimer.Interval = TimeSpan.FromSeconds(
            StatusInfoBar.Severity == InfoBarSeverity.Warning ? 8 : 5);
        _statusDismissTimer.IsRepeating = false;
        _statusDismissTimer.Tick -= OnStatusDismissTimerTick;
        _statusDismissTimer.Tick += OnStatusDismissTimerTick;
        _statusDismissTimer.Start();
    }

    private void OnStatusDismissTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        if (_activeNotification is null)
        {
            StatusInfoBar.IsOpen = false;
        }
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
        DoNotDisturbIcon.Glyph = _settings.DoNotDisturb ? "\uEB71" : "\uEA8F";
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
        try
        {
            var downloadsFolder = WindowsDownloadsFolder.GetPath();
            Directory.CreateDirectory(downloadsFolder);
            var destination = DownloadPathResolver.CreateUniquePath(
                downloadsFolder,
                request.SuggestedFileName,
                _reservedDownloadPaths);
            _reservedDownloadPaths.Add(destination);
            request.SaveTo(destination);

            StatusInfoBar.Title = AppText.Format("Download_InProgressTitle", Path.GetFileName(destination));
            StatusInfoBar.Message = AppText.Format(
                "Download_SourceMessage",
                request.ProfileName,
                request.SourceOrigin.Host);
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
        }
        catch (Exception exception)
        {
            request.Cancel();
            StatusInfoBar.Title = AppText.Get("Download_CancelledTitle");
            StatusInfoBar.Message = exception.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
        }
    }

    private void OnDownloadStateChanged(object? sender, ServiceDownloadStateChangedEventArgs e)
    {
        var state = e.State;
        if (state.Status is ServiceDownloadStatus.Completed or ServiceDownloadStatus.Interrupted)
        {
            _reservedDownloadPaths.Remove(state.DestinationPath);
        }

        switch (state.Status)
        {
            case ServiceDownloadStatus.Completed:
                StatusInfoBar.Title = AppText.Format("Download_CompletedTitle", state.FileName);
                StatusInfoBar.Message = state.DestinationPath;
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                if (!_settings.DoNotDisturb && _windowsNotifications is { IsRegistered: true })
                {
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

                break;
            case ServiceDownloadStatus.Interrupted:
                StatusInfoBar.Title = AppText.Format("Download_InterruptedTitle", state.FileName);
                StatusInfoBar.Message = state.ErrorMessage ?? AppText.Get("Download_InterruptedMessage");
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                break;
            default:
                StatusInfoBar.Title = AppText.Format("Download_InProgressTitle", state.FileName);
                StatusInfoBar.Message = FormatDownloadProgress(state);
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                break;
        }
    }

    private static string FormatDownloadProgress(ServiceDownloadState state)
    {
        var received = FormatBytes(state.BytesReceived);
        return state.TotalBytes is > 0
            ? AppText.Format(
                "Download_ProgressKnown",
                received,
                FormatBytes(state.TotalBytes.Value),
                state.ProfileName)
            : AppText.Format("Download_ProgressUnknown", received, state.ProfileName);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var unit = 0;
        var displayValue = (double)Math.Max(0, bytes);
        while (displayValue >= 1024 && unit < units.Length - 1)
        {
            displayValue /= 1024;
            unit++;
        }

        return $"{displayValue:0.#} {units[unit]}";
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

        await _contentDialogGate.WaitAsync();
        try
        {
            if (request.IsDecided)
            {
                return;
            }

            if (!_isPageLoaded || XamlRoot is null)
            {
                request.Deny(rememberForProfile: false);
                return;
            }

            var rememberChoice = new CheckBox
            {
                Content = AppText.Get("Permission_Remember"),
                IsChecked = true
            };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = AppText.Format(
                    "Permission_RequestDescription",
                    request.Origin.Host,
                    PermissionName(request.Kind)),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = AppText.Format("Permission_Details", request.ProfileName, request.Origin),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            content.Children.Add(rememberChoice);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = AppText.Format("Permission_Title", PermissionName(request.Kind)),
                Content = content,
                PrimaryButtonText = AppText.Get("Permission_Allow"),
                CloseButtonText = AppText.Get("Permission_Deny"),
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            var remember = rememberChoice.IsChecked == true;
            if (result == ContentDialogResult.Primary)
            {
                request.Allow(remember);
            }
            else
            {
                request.Deny(remember);
            }
        }
        catch
        {
            request.Deny(rememberForProfile: false);
        }
        finally
        {
            _contentDialogGate.Release();
        }
    }

    private static string PermissionName(ServicePermissionKind kind) => kind switch
    {
        ServicePermissionKind.Microphone => AppText.Get("Permission_Microphone"),
        ServicePermissionKind.Camera => AppText.Get("Permission_Camera"),
        ServicePermissionKind.Geolocation => AppText.Get("Permission_Geolocation"),
        ServicePermissionKind.Notifications => AppText.Get("Permission_Notifications"),
        ServicePermissionKind.OtherSensors => AppText.Get("Permission_OtherSensors"),
        ServicePermissionKind.ClipboardRead => AppText.Get("Permission_Clipboard"),
        ServicePermissionKind.MultipleAutomaticDownloads => AppText.Get("Permission_AutomaticDownloads"),
        ServicePermissionKind.FileReadWrite => AppText.Get("Permission_Files"),
        ServicePermissionKind.Autoplay => AppText.Get("Permission_Autoplay"),
        ServicePermissionKind.LocalFonts => AppText.Get("Permission_LocalFonts"),
        ServicePermissionKind.MidiSystemExclusiveMessages => AppText.Get("Permission_Midi"),
        ServicePermissionKind.WindowManagement => AppText.Get("Permission_WindowManagement"),
        _ => AppText.Get("Permission_DeviceResource")
    };

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
        host.StateChanged += OnHostStateChanged;
        host.PopupRequested += OnPopupRequested;
        host.PermissionRequested += OnPermissionRequested;
        host.DownloadRequested += OnDownloadRequested;
        host.DownloadStateChanged += OnDownloadStateChanged;
        host.NotificationReceived += OnNotificationReceived;
        host.FaviconChanged += OnFaviconChanged;
        host.ExternalNavigationRequested += OnExternalNavigationRequested;
        host.ConsoleMessageReceived += OnConsoleMessageReceived;
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

    private sealed class ServiceRailItem
    {
        public ServiceRailItem(
            ServiceDefinition service,
            ServiceViewHost host,
            Microsoft.UI.Xaml.FrameworkElement content)
        {
            Host = host;
            Content = content;
            Tile = new ServiceTileState(service);
        }

        public ServiceViewHost Host { get; }

        public Microsoft.UI.Xaml.FrameworkElement Content { get; set; }

        public ServiceTileState Tile { get; }

        public uint UnreadCount { get; private set; }

        public void UpdateService(ServiceDefinition service)
        {
            Tile.UpdateService(service);
        }

        public void UpdateState(ServiceViewState state)
        {
            UnreadCount = state.UnreadCount;
            Tile.UpdateState(state);
        }
    }

    private sealed class ServiceRailGroupItem : INotifyPropertyChanged
    {
        private ServiceRailItem _activeItem;
        private string _displayName;
        private string _unreadText = string.Empty;
        private ulong _unreadCount;
        private Microsoft.UI.Xaml.Visibility _unreadVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        public ServiceRailGroupItem(string key, ServiceRailItem firstItem)
        {
            Key = key;
            Items = [firstItem];
            _activeItem = firstItem;
            _displayName = firstItem.Tile.Name;
            RefreshUnread();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; }

        public ObservableCollection<ServiceRailItem> Items { get; }

        public ServiceRailItem ActiveItem
        {
            get => _activeItem;
            set
            {
                if (ReferenceEquals(_activeItem, value))
                {
                    return;
                }

                _activeItem = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveItem)));
                RefreshDisplayName();
            }
        }

        public string DisplayName
        {
            get => _displayName;
            private set
            {
                if (_displayName == value)
                {
                    return;
                }

                _displayName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
            }
        }

        public string AccessibleName => _unreadCount > 0
            ? AppText.Format("ServiceRail_ItemWithUnreadAutomationName", DisplayName, _unreadCount)
            : DisplayName;

        public string UnreadText
        {
            get => _unreadText;
            private set
            {
                if (_unreadText == value)
                {
                    return;
                }

                _unreadText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnreadText)));
            }
        }

        public Microsoft.UI.Xaml.Visibility UnreadVisibility
        {
            get => _unreadVisibility;
            private set
            {
                if (_unreadVisibility == value)
                {
                    return;
                }

                _unreadVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnreadVisibility)));
            }
        }

        public Microsoft.UI.Xaml.Visibility StackVisibility => Items.Count > 1
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public void RefreshDisplayName()
        {
            DisplayName = Items.Count > 1
                ? AppText.Format("ServiceGroup_DisplayName", ActiveItem.Tile.Name, Items.Count)
                : ActiveItem.Tile.Name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StackVisibility)));
        }

        public void RefreshUnread()
        {
            var total = Items.Aggregate(0UL, (sum, item) => sum + item.UnreadCount);
            _unreadCount = total;
            UnreadText = total switch
            {
                0 => string.Empty,
                > 99 => "99+",
                _ => total.ToString()
            };
            UnreadVisibility = total > 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
        }
    }
}
