
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

/// <summary>
/// Displays the main application content.
/// </summary>
public sealed partial class MainPage : Page
{
    private const string NotificationsEnabledGlyph = "\uEA8F";
    private const string DoNotDisturbGlyph = "\uE7ED";

    private readonly List<ServiceViewHost> _hosts = [];
    private readonly ObservableCollection<ServiceRailItem> _railItems = [];
    private readonly ObservableCollection<ServiceRailGroupItem> _railGroups = [];
    private readonly Dictionary<string, ServiceViewState> _states = [];
    private readonly List<ServicePopupWindow> _popupWindows = [];
    private readonly List<ServiceDefinition> _services = [];
    private readonly MainPageDialogService _dialogs;
    private readonly DownloadCoordinator _downloads = new();
    private readonly StatusInfoBarController _status;
    private readonly Dictionary<Guid, ServiceNotificationReceivedEventArgs> _webNotifications = [];
    private AddServiceWindow? _addServiceWindow;
    private ServiceConfigurationStore? _serviceStore;
    private SettingsConfigurationStore? _settingsStore;
    private ApplicationSettings _settings = new();
    private WebViewEnvironmentProvider? _environment;
    private ServiceViewContentMapping? _contentMapping;
    private WebConsoleDiagnosticLog? _webConsoleLog;
    private WindowsAppNotificationService? _windowsNotifications;
    private string? _pendingActivatedProfile;
    private bool _initialized;
    private bool _isPageLoaded;
    private bool _loadingSettings;
    private bool _changingInstanceSelection;
    private bool _diagnosticsEnabled;

    public MainPage()
    {
        InitializeComponent();
        _dialogs = new MainPageDialogService(() => XamlRoot);
        _status = new StatusInfoBarController(StatusInfoBar, DispatcherQueue, SelectProfile);
        ApplyLocalizedShellText();
        ServiceRail.ItemsSource = _railGroups;
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
    }

    private static void LocalizeHeaderButton(
        Microsoft.UI.Xaml.DependencyObject button,
        string automationResource,
        string tooltipResource)
    {
        AutomationProperties.SetName(button, AppText.Get(automationResource));
        ToolTipService.SetToolTip(button, AppText.Get(tooltipResource));
    }
}
