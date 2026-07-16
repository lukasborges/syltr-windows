using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Catalog;
using Syltr.Config;
using Syltr.Config.Downloads;
using Syltr.Config.Notifications;
using Syltr.Engine;
using Syltr.Engine.Spike;
using Syltr.Icon;

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
    private WindowsAppNotificationService? _windowsNotifications;
    private ServiceNotificationReceivedEventArgs? _activeNotification;
    private string? _pendingActivatedProfile;
    private bool _initialized;
    private bool _isPageLoaded;
    private bool _loadingSettings;
    private bool _changingInstanceSelection;

    public MainPage()
    {
        InitializeComponent();
        ServiceRail.ItemsSource = _railGroups;
        StatusInfoBar.RegisterPropertyChangedCallback(InfoBar.TitleProperty, OnStatusChanged);
        StatusInfoBar.RegisterPropertyChangedCallback(InfoBar.SeverityProperty, OnStatusChanged);
    }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _isPageLoaded = true;
        ((App)Microsoft.UI.Xaml.Application.Current).SetMainWindowDragRegion(TitleDragRegion);
        DiagnosticsMenuItem.Visibility = Environment.GetEnvironmentVariable("SYLTR_DEBUG") == "1"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        _windowsNotifications = ((App)Microsoft.UI.Xaml.Application.Current).Notifications;
        _windowsNotifications.Activated += OnWindowsNotificationActivated;
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var paths = ApplicationDataPaths.ForCurrentUser();
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
            if (loadedSettings.Status == ConfigurationLoadStatus.Created &&
                _settings.SpellLanguages.Count == 0)
            {
                _settings = _settings with
                {
                    SpellLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages
                        .Select(language => language.Replace('-', '_'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
                await _settingsStore.SaveAsync(_settings);
            }
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
                StatusInfoBar.Title = "Nenhum serviço configurado";
                StatusInfoBar.Message = "Adicione um serviço para começar.";
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
                StatusInfoBar.Title = "Perfis insuficientes";
                StatusInfoBar.Message = "São necessários ao menos dois perfis funcionais para comprovar isolamento.";
                StatusInfoBar.Severity = InfoBarSeverity.Error;
            }

            if (readyHosts.Length != enabledHosts.Length)
            {
                StatusInfoBar.Title = "Alguns perfis falharam";
                StatusInfoBar.Message = $"{readyHosts.Length} de {enabledHosts.Length} perfis ativos iniciaram. Use Recuperar nas abas com falha.";
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
            StatusInfoBar.Title = "Falha ao iniciar WebView2";
            StatusInfoBar.Message = $"{exception.Message} Diagnóstico: {SpikeDiagnosticLog.GetPath(paths)}";
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
        var addInstance = new MenuFlyoutItem { Text = "Adicionar outra instância…" };
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
        var reload = new MenuFlyoutItem { Text = "Recarregar" };
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
        var home = new MenuFlyoutItem { Text = "Ir para o início" };
        home.Click += (_, _) => SelectedReadyHost()?.NavigateHome();
        var edit = new MenuFlyoutItem { Text = "Editar serviço…" };
        edit.Click += async (_, _) => await ShowEditServiceDialogAsync();
        var mute = new MenuFlyoutItem
        {
            Text = service.Muted ? "Reativar notificações" : "Silenciar notificações"
        };
        mute.Click += async (_, _) => await SetSelectedServiceMutedAsync(!service.Muted);
        var disable = new MenuFlyoutItem
        {
            Text = service.Disabled ? "Ativar serviço" : "Desativar serviço"
        };
        disable.Click += async (_, _) => await SetSelectedServiceDisabledAsync(!service.Disabled);
        var remove = new MenuFlyoutItem { Text = "Remover serviço" };
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
        e.Handled = true;
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
        StatusInfoBar.Title = muted ? "Notificações silenciadas" : "Notificações reativadas";
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
        StatusInfoBar.Title = disabled ? "Serviço desativado" : "Serviço ativado";
        StatusInfoBar.Message = disabled
            ? "A sessão permanece salva e pode ser reativada pelo mesmo menu."
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

        var version = typeof(MainPage).Assembly.GetName().Version?.ToString(3) ?? "desenvolvimento";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Syltr",
            Content = $"Versão {version}\n\nSeus serviços web em uma única janela, com sessões isoladas.",
            CloseButtonText = "Fechar",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private void OnExitClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ((App)Microsoft.UI.Xaml.Application.Current).CloseMainWindow();

    private async void OnSpellLanguagesClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_settingsStore is null || XamlRoot is null)
        {
            return;
        }

        var languageIds = Windows.System.UserProfile.GlobalizationPreferences.Languages
            .Select(language => language.Replace('-', '_'))
            .Concat(_settings.SpellLanguages)
            .Append("pt_BR")
            .Append("en_US")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var languageList = new StackPanel { Spacing = 4, MinWidth = 360 };
        var choices = new List<(string Id, CheckBox CheckBox)>();
        foreach (var languageId in languageIds)
        {
            var cultureName = languageId.Replace('_', '-');
            string displayName;
            try
            {
                displayName = System.Globalization.CultureInfo.GetCultureInfo(cultureName).NativeName;
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                displayName = languageId;
            }

            var checkBox = new CheckBox
            {
                Content = $"{displayName}  ({languageId})",
                IsChecked = _settings.SpellLanguages.Contains(languageId, StringComparer.OrdinalIgnoreCase)
            };
            choices.Add((languageId, checkBox));
            languageList.Children.Add(checkBox);
        }

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Idiomas preferidos para a correção ortográfica do conteúdo web.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });
        content.Children.Add(languageList);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Idiomas de correção",
            Content = content,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _settings = _settings with
        {
            SpellLanguages = choices
                .Where(choice => choice.CheckBox.IsChecked == true)
                .Select(choice => choice.Id)
                .ToArray()
        };
        await _settingsStore.SaveAsync(_settings);
        StatusInfoBar.Title = "Idiomas de correção atualizados";
        StatusInfoBar.Message = _settings.SpellLanguages.Count == 0
            ? "O WebView2 usará as preferências padrão do sistema."
            : string.Join(", ", _settings.SpellLanguages);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
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

        var nameBox = new TextBox { Header = "Nome", PlaceholderText = "Ex.: Teams trabalho" };
        var urlBox = new TextBox { Header = "Endereço", PlaceholderText = "https://..." };
        var content = new StackPanel { Spacing = 12, MinWidth = 400 };
        content.Children.Add(new TextBlock
        {
            Text = "Adicione qualquer serviço web por URL.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });
        content.Children.Add(nameBox);
        content.Children.Add(urlBox);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Serviço personalizado",
            Content = content,
            PrimaryButtonText = "Adicionar",
            CloseButtonText = "Cancelar",
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
            StatusInfoBar.Title = "Serviço inválido";
            StatusInfoBar.Message = "Informe um nome e um endereço HTTP ou HTTPS válido.";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        var name = requestedName.Trim();
        if (promptForDuplicateName && _services.Any(service => service.Url == normalizedUrl))
        {
            var nameBox = new TextBox { Header = "Nome", Text = name };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Nomear esta instância",
                Content = nameBox,
                PrimaryButtonText = "Adicionar",
                CloseButtonText = "Cancelar",
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
            StatusInfoBar.Title = "Serviço inválido";
            StatusInfoBar.Message = "Informe um nome para o serviço.";
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
            StatusInfoBar.Title = $"{service.Name} adicionado";
            StatusInfoBar.Message = $"Perfil isolado {host.ProfileName} criado e salvo.";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        else
        {
            StatusInfoBar.Title = $"{service.Name} foi salvo, mas não carregou";
            StatusInfoBar.Message = host.State.ErrorMessage ?? "Use o menu contextual para tentar novamente.";
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
        var nameBox = new TextBox { Header = "Nome", Text = service.Name };
        var urlBox = new TextBox { Header = "Endereço", Text = service.Url };
        var userAgentBox = new TextBox
        {
            Header = "User-Agent personalizado (opcional)",
            Text = service.UserAgent ?? string.Empty,
            PlaceholderText = "Deixe vazio para usar o padrão do WebView2"
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 420 };
        content.Children.Add(nameBox);
        content.Children.Add(urlBox);
        content.Children.Add(userAgentBox);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Editar {service.Name}",
            Content = content,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !ServiceUrl.TryNormalize(urlBox.Text, out var normalizedUrl))
        {
            StatusInfoBar.Title = "Serviço inválido";
            StatusInfoBar.Message = "Informe um nome e um endereço HTTP ou HTTPS válido.";
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

        StatusInfoBar.Title = $"{updated.Name} atualizado";
        StatusInfoBar.Message = updated.Disabled
            ? $"{updated.Name} foi desativado; sua sessão permanece salva."
            : $"A sessão de {updated.Name} foi preservada e abriu o endereço configurado.";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
    }

    private async void OnRecoverClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await RecoverSelectedHostAsync();

    private async Task RecoverSelectedHostAsync()
    {
        var host = SelectedHost();
        if (host is null || host.State.Status != ServiceViewStatus.Failed)
        {
            StatusInfoBar.Title = "Recuperação não necessária";
            StatusInfoBar.Message = "O serviço selecionado não está em estado de falha.";
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            return;
        }

        try
        {
            await host.RecoverAsync();
            StatusInfoBar.Title = "Perfil recuperado";
            StatusInfoBar.Message = $"{host.ProfileName} foi recriado usando a mesma sessão persistente.";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        catch (Exception exception)
        {
            StatusInfoBar.Title = "Falha na recuperação";
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

    private async void OnMeasureMemoryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_environment is null || XamlRoot is null)
        {
            return;
        }

        try
        {
            var snapshot = await _environment.CaptureMemorySnapshotAsync();
            var processGroups = snapshot.Processes
                .GroupBy(process => process.Kind)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}: {group.Count()} processo(s), {FormatBytes(group.Sum(process => process.WorkingSetBytes))}");
            var details = string.Join(Environment.NewLine, processGroups);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Memória WebView2 · {_hosts.Count} serviços",
                Content = $"Working set: {FormatBytes(snapshot.TotalWorkingSetBytes)}\n" +
                          $"Memória privada: {FormatBytes(snapshot.TotalPrivateMemoryBytes)}\n" +
                          $"Processos: {snapshot.Processes.Count}\n\n{details}",
                CloseButtonText = "Fechar",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            StatusInfoBar.Title = "Não foi possível medir a memória";
            StatusInfoBar.Message = exception.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
        }
    }

    private void OnTestWindowsNotificationClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_windowsNotifications is not { IsRegistered: true })
        {
            StatusInfoBar.Title = "Notificações do Windows indisponíveis";
            StatusInfoBar.Message = _windowsNotifications?.RegistrationError ?? "O registro não foi concluído.";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        try
        {
            var profileName = SelectedHost()?.ProfileName ?? "syltr";
            _windowsNotifications.Show(
                profileName,
                "Teste do Syltr",
                $"A ponte nativa está funcionando para {profileName}.");
            StatusInfoBar.Title = "Notificação enviada ao Windows";
            StatusInfoBar.Message = "Clique nela para confirmar a ativação do perfil de origem.";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
        }
        catch (Exception exception)
        {
            StatusInfoBar.Title = "Falha ao enviar notificação";
            StatusInfoBar.Message = exception.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
        }
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

        StatusInfoBar.Title = "Teste de autenticação";
        StatusInfoBar.Message = "Cada aba usa um perfil persistente diferente. Faça login com contas distintas para validar a separação.";
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
    }

    private async void OnDeleteProfileClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        await DeleteSelectedProfileAsync();

    private async Task DeleteSelectedProfileAsync()
    {
        var railItem = SelectedRailItem();
        var host = railItem?.Host;
        if (railItem is null || host is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Remover {railItem.Tile.Name}?",
            Content = "Cookies, sessão e armazenamento deste serviço serão removidos. Os demais serviços não serão alterados.",
            PrimaryButtonText = "Remover serviço",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        host.StateChanged -= OnHostStateChanged;
        host.PopupRequested -= OnPopupRequested;
        host.PermissionRequested -= OnPermissionRequested;
        host.DownloadRequested -= OnDownloadRequested;
        host.DownloadStateChanged -= OnDownloadStateChanged;
        host.NotificationReceived -= OnNotificationReceived;
        host.FaviconChanged -= OnFaviconChanged;
        host.ExternalNavigationRequested -= OnExternalNavigationRequested;
        if (host.State.Status == ServiceViewStatus.Disabled && !host.IsInitialized)
        {
            railItem.Content = host.Content;
            ServiceContentPresenter.Content = host.Content;
            await host.ResumeAsync(navigateHome: false);
        }

        host.DeleteProfile();
        _hosts.Remove(host);
        _states.Remove(host.ProfileName);
        _services.RemoveAll(service =>
            WebViewProfileName.FromServiceId(service.Id) == host.ProfileName);
        if (_serviceStore is not null)
        {
            await _serviceStore.SaveAsync(_services);
        }

        var group = _railGroups.First(group => group.Items.Contains(railItem));
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

        if (ServiceRail.SelectedItem is null && _railGroups.Count > 0)
        {
            ServiceRail.SelectedIndex = 0;
        }

        SelectRailContent();
        UpdateServiceOverlay();

        StatusInfoBar.Title = "Serviço removido";
        StatusInfoBar.Message = $"{railItem.Tile.Name} e sua sessão foram removidos.";
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        UpdateSelectedProfileStatus();
    }

    private async void OnPopupRequested(object? sender, ServicePopupRequestedEventArgs e)
    {
        if (e.RequestedUri is null)
        {
            e.Cancel();
            return;
        }

        if (!e.IsSameOrigin)
        {
            await _contentDialogGate.WaitAsync();
            try
            {
                if (!_isPageLoaded || XamlRoot is null)
                {
                    e.Cancel();
                    return;
                }

                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Onde abrir este link?",
                    Content = $"Origem: {e.RequestedOrigin?.ToString() ?? e.RequestedUri.Scheme}\nPerfil: {e.PopupHost.ProfileName}\n\nUse Syltr para OAuth/SSO. Use o navegador para links externos comuns.",
                    PrimaryButtonText = "Abrir no Syltr",
                    SecondaryButtonText = "Navegador padrão",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Primary
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Secondary)
                {
                    var launched = await Windows.System.Launcher.LaunchUriAsync(e.RequestedUri);
                    if (launched)
                    {
                        e.OpenExternal();
                    }
                    else
                    {
                        e.Cancel();
                    }

                    return;
                }

                if (result != ContentDialogResult.Primary)
                {
                    e.Cancel();
                    return;
                }
            }
            catch
            {
                e.Cancel();
                return;
            }
            finally
            {
                _contentDialogGate.Release();
            }
        }

        e.PopupHost.PopupRequested += OnPopupRequested;
        e.PopupHost.PermissionRequested += OnPermissionRequested;
        e.PopupHost.DownloadRequested += OnDownloadRequested;
        e.PopupHost.DownloadStateChanged += OnDownloadStateChanged;
        e.PopupHost.NotificationReceived += OnNotificationReceived;
        e.PopupHost.ExternalNavigationRequested += OnExternalNavigationRequested;
        var popupWindow = new ServicePopupWindow(e.PopupHost, e.RequestedUri);
        popupWindow.Closed += (_, _) =>
        {
            e.PopupHost.PopupRequested -= OnPopupRequested;
            e.PopupHost.PermissionRequested -= OnPermissionRequested;
            e.PopupHost.DownloadRequested -= OnDownloadRequested;
            e.PopupHost.DownloadStateChanged -= OnDownloadStateChanged;
            e.PopupHost.NotificationReceived -= OnNotificationReceived;
            e.PopupHost.ExternalNavigationRequested -= OnExternalNavigationRequested;
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
        if (sender is not ServiceViewHost host)
        {
            return;
        }

        await _contentDialogGate.WaitAsync();
        try
        {
            if (!_isPageLoaded || XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Este link sai do serviço atual",
                Content = $"Destino: {e.DestinationOrigin?.ToString() ?? e.Destination.Scheme}\nPerfil: {e.ProfileName}",
                PrimaryButtonText = "Continuar no Syltr",
                SecondaryButtonText = "Navegador padrão",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Secondary
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                host.Navigate(e.Destination);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await Windows.System.Launcher.LaunchUriAsync(e.Destination);
            }
        }
        catch
        {
            // Canceling the original navigation is the safe fallback.
        }
        finally
        {
            _contentDialogGate.Release();
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

        var openButton = new Button { Content = "Abrir serviço" };
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
            ? $"Notificação de {notification.SenderOrigin.Host} · {notification.ProfileName}"
            : $"{notification.Body} · {notification.ProfileName}";
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
            ? "Não perturbe ativado"
            : "Não perturbe desativado";
        StatusInfoBar.Message = _settings.DoNotDisturb
            ? "Notificações web serão dispensadas até você desativar este modo."
            : "Notificações dos serviços não silenciados voltarão a aparecer.";
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

            StatusInfoBar.Title = $"Baixando {Path.GetFileName(destination)}";
            StatusInfoBar.Message = $"Perfil {request.ProfileName} · {request.SourceOrigin.Host}";
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
        }
        catch (Exception exception)
        {
            request.Cancel();
            StatusInfoBar.Title = "Download cancelado";
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
                StatusInfoBar.Title = $"Download concluído: {state.FileName}";
                StatusInfoBar.Message = state.DestinationPath;
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                if (!_settings.DoNotDisturb && _windowsNotifications is { IsRegistered: true })
                {
                    try
                    {
                        _windowsNotifications.Show(
                            state.ProfileName,
                            "Download concluído",
                            state.FileName);
                    }
                    catch
                    {
                        // The in-app completion state remains visible as a fallback.
                    }
                }

                break;
            case ServiceDownloadStatus.Interrupted:
                StatusInfoBar.Title = $"Download interrompido: {state.FileName}";
                StatusInfoBar.Message = state.ErrorMessage ?? "O navegador interrompeu o download.";
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                break;
            default:
                StatusInfoBar.Title = $"Baixando {state.FileName}";
                StatusInfoBar.Message = FormatDownloadProgress(state);
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                break;
        }
    }

    private static string FormatDownloadProgress(ServiceDownloadState state)
    {
        var received = FormatBytes(state.BytesReceived);
        return state.TotalBytes is > 0
            ? $"{received} de {FormatBytes(state.TotalBytes.Value)} · {state.ProfileName}"
            : $"{received} recebidos · {state.ProfileName}";
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
                Content = "Lembrar para este perfil",
                IsChecked = true
            };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = $"{request.Origin.Host} quer acessar {PermissionName(request.Kind)}.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Perfil: {request.ProfileName}\nOrigem: {request.Origin}",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            content.Children.Add(rememberChoice);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Permitir {PermissionName(request.Kind)}?",
                Content = content,
                PrimaryButtonText = "Permitir",
                CloseButtonText = "Negar",
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
        ServicePermissionKind.Microphone => "o microfone",
        ServicePermissionKind.Camera => "a câmera",
        ServicePermissionKind.Geolocation => "sua localização",
        ServicePermissionKind.Notifications => "as notificações",
        ServicePermissionKind.OtherSensors => "os sensores do dispositivo",
        ServicePermissionKind.ClipboardRead => "a área de transferência",
        ServicePermissionKind.MultipleAutomaticDownloads => "downloads automáticos",
        ServicePermissionKind.FileReadWrite => "arquivos e pastas selecionados",
        ServicePermissionKind.Autoplay => "a reprodução automática",
        ServicePermissionKind.LocalFonts => "as fontes locais",
        ServicePermissionKind.MidiSystemExclusiveMessages => "dispositivos MIDI",
        ServicePermissionKind.WindowManagement => "o gerenciamento de janelas",
        _ => "um recurso do dispositivo"
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
            StatusInfoBar.Title = "Falha no isolamento";
            StatusInfoBar.Message = "Os perfis não mantiveram valores de armazenamento distintos.";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            return;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        if (result.PersistenceWasDetected)
        {
            StatusInfoBar.Title = "Isolamento e persistência confirmados";
            StatusInfoBar.Message = $"{result.Profiles.Count} perfis recuperaram valores distintos da execução anterior.";
            return;
        }

        StatusInfoBar.Title = "Isolamento confirmado nesta execução";
        StatusInfoBar.Message = $"Reinicie o aplicativo para confirmar também a persistência dos {result.Profiles.Count} perfis.";
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
        var title = string.IsNullOrWhiteSpace(state.DocumentTitle) ? "sem título" : state.DocumentTitle;
        ProfileStatusText.Text =
            $"{host.ProfileName} · {state.Status} · {title} · não lidos: {state.UnreadCount}";
    }

    private void UpdateServiceOverlay()
    {
        var host = SelectedHost();
        if (host is null)
        {
            ServiceStateOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ServiceStateProgressRing.IsActive = false;
            ServiceStateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ServiceStateTitle.Text = "Nenhum serviço configurado";
            ServiceStateMessage.Text = "Adicione um serviço do catálogo ou informe uma URL personalizada.";
            ServiceStateActionButton.Content = "Adicionar serviço";
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
                ? "Recuperando serviço"
                : "Carregando serviço";
            ServiceStateMessage.Text = $"Perfil {host.ProfileName}";
            ServiceStateActionButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        ServiceStateProgressRing.IsActive = false;
        ServiceStateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ServiceStateTitle.Text = state.Status == ServiceViewStatus.Failed
            ? "Não foi possível carregar o serviço"
            : "Serviço indisponível";
        ServiceStateMessage.Text = state.ErrorMessage ?? $"Perfil {host.ProfileName}";
        ServiceStateActionButton.Content = "Tentar novamente";
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
            service.UserAgent);
        host.StateChanged += OnHostStateChanged;
        host.PopupRequested += OnPopupRequested;
        host.PermissionRequested += OnPermissionRequested;
        host.DownloadRequested += OnDownloadRequested;
        host.DownloadStateChanged += OnDownloadStateChanged;
        host.NotificationReceived += OnNotificationReceived;
        host.FaviconChanged += OnFaviconChanged;
        host.ExternalNavigationRequested += OnExternalNavigationRequested;
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
            Text = $"{service.Name} está desativado",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Use o menu de clique direito para reativar sem perder a sessão.",
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
            }
        }

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
                ? $"{ActiveItem.Tile.Name} · {Items.Count} instâncias"
                : ActiveItem.Tile.Name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StackVisibility)));
        }

        public void RefreshUnread()
        {
            var total = Items.Aggregate(0UL, (sum, item) => sum + item.UnreadCount);
            UnreadText = total switch
            {
                0 => string.Empty,
                > 99 => "99+",
                _ => total.ToString()
            };
            UnreadVisibility = total > 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }
}
