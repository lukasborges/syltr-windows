namespace Syltr.Tests;

public sealed class AccessibilitySourceTests
{
    [Fact]
    public void Main_shell_exposes_headings_live_status_and_keyboard_context_actions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Syltr", "Window", "MainPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Syltr", "Window", "MainPage.xaml.cs"));
        var integrationCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "MainPage.Integrations.cs"));
        var railCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "ServiceRailGroupItem.cs"));

        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("OnServiceContextMenuAccelerator", xaml);
        Assert.Contains("Key=\"F10\" Modifiers=\"Shift\"", xaml);
        Assert.Contains("AutomationProperties.SetHelpText(ServiceRail", code);
        Assert.Contains("AutomationProperties.SetName(InstanceSelector", code);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", xaml);
        Assert.Contains("ServiceRail_ItemWithUnreadAutomationName", railCode);
        Assert.Contains("ResourceDictionary x:Key=\"HighContrast\"", xaml);
        Assert.Contains("SystemColorHighlightColorBrush", xaml);
        Assert.Contains("Style x:Key=\"HeaderSymbolIconViewboxStyle\" TargetType=\"Viewbox\"", xaml);
        Assert.Contains("Style=\"{StaticResource HeaderSymbolIconViewboxStyle}\"", xaml);
        Assert.Contains("Style=\"{StaticResource HeaderFontIconStyle}\"", xaml);
        Assert.Contains("Segoe Fluent Icons, Segoe MDL2 Assets", xaml);
        Assert.Contains("Foreground\" Value=\"{ThemeResource TextFillColorPrimaryBrush}\"", xaml);
        Assert.Contains("DoNotDisturbGlyph = \"\\uE7ED\"", code);
        Assert.DoesNotContain("\\uEB71", code);
        Assert.Contains("MaxWidth=\"520\"", xaml);
        Assert.Contains("ReorderMode=\"Enabled\"", xaml);
        Assert.Contains("CanReorderItems=\"True\"", xaml);
        Assert.Contains("AllowDrop=\"True\"", xaml);
        Assert.DoesNotContain("ImportServicesMenuItem", xaml);
        Assert.DoesNotContain("DiagnosticsMenuItem", xaml);
        Assert.Contains("RemoveRailItem(railItem)", integrationCode);
    }

    [Fact]
    public void Service_catalog_has_initial_focus_escape_and_accessible_structure()
    {
        var root = FindRepositoryRoot();
        var windowCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "AddServiceWindow.cs"));
        var catalogCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "ServiceCatalogView.cs"));

        Assert.Contains("AddService_SearchAutomationName", catalogCode);
        Assert.Contains("AutomationProperties.SetHeadingLevel", catalogCode);
        Assert.Contains("VirtualKey.Escape", catalogCode);
        Assert.Contains("Focus(Microsoft.UI.Xaml.FocusState.Programmatic)", catalogCode);
        Assert.Contains("DispatcherQueue.TryEnqueue(_catalogView.FocusSearch)", windowCode);
    }

    [Fact]
    public void About_dialog_exposes_the_linux_reference_metadata()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "MainPageDialogService.cs"));

        Assert.Contains("ms-appx:///Assets/Syltr.svg", code);
        Assert.Contains("About_Developer", code);
        Assert.Contains("About_Description", code);
        Assert.Contains("https://github.com/lukasborges/syltr", code);
        Assert.Contains("https://github.com/lukasborges/syltr/issues", code);
        Assert.Contains("About_License", code);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Syltr.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
