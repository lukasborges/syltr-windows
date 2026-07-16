using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Config;
using Syltr.Engine;
using Syltr.Localization;
using Syltr.Spellcheck;

namespace Syltr.Window;

internal sealed class MainPageDialogService
{
    private readonly Func<Microsoft.UI.Xaml.XamlRoot?> _xamlRootProvider;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    public MainPageDialogService(Func<Microsoft.UI.Xaml.XamlRoot?> xamlRootProvider)
    {
        _xamlRootProvider = xamlRootProvider;
    }

    public async Task ShowAboutAsync()
    {
        var dialog = CreateDialog(
            AppText.Get("About_Title"),
            CreateAboutContent(),
            closeText: AppText.Get("Common_Close"),
            defaultButton: ContentDialogButton.Close);
        if (dialog is not null)
        {
            await dialog.ShowAsync();
        }
    }

    public async Task ShowSpellcheckAsync()
    {
        var languages = WindowsSpellcheckPreferences.GetPreferredLanguages();
        var languageSummary = languages.Count == 0
            ? AppText.Get("Spellcheck_NoPreferredLanguages")
            : string.Join(
                Environment.NewLine,
                languages.Select(language => $"• {language.DisplayName} ({language.Id})"));
        var content = new TextBlock
        {
            Text = AppText.Format("Spellcheck_Description", languageSummary),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MaxWidth = 460
        };
        var dialog = CreateDialog(
            AppText.Get("Spellcheck_Title"),
            content,
            primaryText: AppText.Get("Spellcheck_OpenSettings"),
            closeText: AppText.Get("Common_Close"),
            defaultButton: ContentDialogButton.Close);
        if (dialog is not null && await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Windows.System.Launcher.LaunchUriAsync(WindowsSpellcheckPreferences.SettingsUri);
        }
    }

    public async Task<CustomServiceInput?> RequestCustomServiceAsync()
    {
        var nameBox = new TextBox
        {
            Header = AppText.Get("Field_Name"),
            PlaceholderText = AppText.Get("CustomService_NamePlaceholder")
        };
        var urlBox = new TextBox
        {
            Header = AppText.Get("Field_Address"),
            PlaceholderText = "https://..."
        };
        var content = CreateStack(
            new TextBlock
            {
                Text = AppText.Get("CustomService_Description"),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            },
            nameBox,
            urlBox);
        var dialog = CreateDialog(
            AppText.Get("CustomService_Title"),
            content,
            primaryText: AppText.Get("Common_Add"),
            closeText: AppText.Get("Common_Cancel"));
        return dialog is not null && await dialog.ShowAsync() == ContentDialogResult.Primary
            ? new CustomServiceInput(nameBox.Text, urlBox.Text)
            : null;
    }

    public async Task<string?> RequestInstanceNameAsync(string currentName)
    {
        var nameBox = new TextBox { Header = AppText.Get("Field_Name"), Text = currentName };
        var dialog = CreateDialog(
            AppText.Get("Instance_NameTitle"),
            nameBox,
            primaryText: AppText.Get("Common_Add"),
            closeText: AppText.Get("Common_Cancel"));
        return dialog is not null && await dialog.ShowAsync() == ContentDialogResult.Primary
            ? nameBox.Text.Trim()
            : null;
    }

    public async Task<EditServiceInput?> RequestEditServiceAsync(ServiceDefinition service)
    {
        var nameBox = new TextBox { Header = AppText.Get("Field_Name"), Text = service.Name };
        var urlBox = new TextBox { Header = AppText.Get("Field_Address"), Text = service.Url };
        var userAgentBox = new TextBox
        {
            Header = AppText.Get("EditService_UserAgent"),
            Text = service.UserAgent ?? string.Empty,
            PlaceholderText = AppText.Get("EditService_UserAgentPlaceholder")
        };
        var dialog = CreateDialog(
            AppText.Format("EditService_Title", service.Name),
            CreateStack(nameBox, urlBox, userAgentBox),
            primaryText: AppText.Get("Common_Save"),
            closeText: AppText.Get("Common_Cancel"));
        return dialog is not null && await dialog.ShowAsync() == ContentDialogResult.Primary
            ? new EditServiceInput(nameBox.Text, urlBox.Text, userAgentBox.Text)
            : null;
    }

    public async Task<bool> ConfirmRemovalAsync(string serviceName, bool removesGroupedAccount)
    {
        var actionKey = removesGroupedAccount ? "Context_RemoveAccount" : "Context_Remove";
        var descriptionKey = removesGroupedAccount
            ? "RemoveService_AccountDescription"
            : "RemoveService_Description";
        var dialog = CreateDialog(
            AppText.Format("RemoveService_Title", serviceName),
            AppText.Get(descriptionKey),
            primaryText: AppText.Get(actionKey),
            closeText: AppText.Get("Common_Cancel"),
            defaultButton: ContentDialogButton.Close);
        return dialog is not null && await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<PermissionDialogResult?> RequestPermissionAsync(
        ServicePermissionRequestedEventArgs request)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (request.IsDecided)
            {
                return null;
            }

            var rememberChoice = new CheckBox
            {
                Content = AppText.Get("Permission_Remember"),
                IsChecked = true
            };
            var permissionName = PermissionName(request.Kind);
            var content = CreateStack(
                new TextBlock
                {
                    Text = AppText.Format(
                        "Permission_RequestDescription",
                        request.Origin.Host,
                        permissionName),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = AppText.Format("Permission_Details", request.ProfileName, request.Origin),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                },
                rememberChoice);
            var dialog = CreateDialog(
                AppText.Format("Permission_Title", permissionName),
                content,
                primaryText: AppText.Get("Permission_Allow"),
                closeText: AppText.Get("Permission_Deny"),
                defaultButton: ContentDialogButton.Close);
            if (dialog is null)
            {
                return null;
            }

            var result = await dialog.ShowAsync();
            return new PermissionDialogResult(
                result == ContentDialogResult.Primary,
                rememberChoice.IsChecked == true);
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private ContentDialog? CreateDialog(
        string title,
        object content,
        string? primaryText = null,
        string? closeText = null,
        ContentDialogButton defaultButton = ContentDialogButton.Primary)
    {
        var xamlRoot = _xamlRootProvider();
        return xamlRoot is null
            ? null
            : new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = defaultButton
            };
    }

    private static StackPanel CreateAboutContent()
    {
        var version = typeof(MainPage).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var content = CreateStack(
            new Image
            {
                Width = 96,
                Height = 96,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                Source = new SvgImageSource(new Uri("ms-appx:///Assets/Syltr.svg"))
            },
            new TextBlock
            {
                Text = "Syltr",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
            },
            new TextBlock
            {
                Text = AppText.Format("About_Version", version),
                Opacity = 0.7,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
            },
            new TextBlock
            {
                Text = AppText.Get("About_Developer"),
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
            },
            new TextBlock
            {
                Text = AppText.Get("About_Description"),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 4, 0, 2),
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            },
            CreateAboutLinks(),
            new TextBlock
            {
                Text = AppText.Get("About_License"),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 6, 0, 0),
                FontSize = 12,
                Opacity = 0.7,
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
        content.MaxWidth = 420;
        return content;
    }

    private static StackPanel CreateAboutLinks()
    {
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
        return links;
    }

    private static StackPanel CreateStack(params Microsoft.UI.Xaml.UIElement[] children)
    {
        var stack = new StackPanel { Spacing = 12, MaxWidth = 480 };
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }

        return stack;
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
}

internal sealed record CustomServiceInput(string Name, string Url);

internal sealed record EditServiceInput(string Name, string Url, string UserAgent);

internal sealed record PermissionDialogResult(bool Allowed, bool RememberForProfile);
