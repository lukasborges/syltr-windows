namespace Syltr.Tests;

public sealed class AccessibilitySourceTests
{
    [Fact]
    public void Main_shell_exposes_headings_live_status_and_keyboard_context_actions()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Syltr", "Window", "MainPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Syltr", "Window", "MainPage.xaml.cs"));

        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("OnServiceContextMenuAccelerator", xaml);
        Assert.Contains("Key=\"F10\" Modifiers=\"Shift\"", xaml);
        Assert.Contains("AutomationProperties.SetHelpText(ServiceRail", code);
        Assert.Contains("AutomationProperties.SetName(InstanceSelector", code);
    }

    [Fact]
    public void Service_catalog_has_initial_focus_escape_and_accessible_structure()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "AddServiceWindow.cs"));

        Assert.Contains("AddService_SearchAutomationName", code);
        Assert.Contains("AutomationProperties.SetHeadingLevel", code);
        Assert.Contains("VirtualKey.Escape", code);
        Assert.Contains("Focus(Microsoft.UI.Xaml.FocusState.Programmatic)", code);
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
