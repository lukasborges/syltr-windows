using Syltr.Config;

namespace Syltr.Tests.Config;

public class ApplicationDataPathsTests
{
    [Fact]
    public void KeepsMutableDataInDedicatedDirectories()
    {
        var paths = new ApplicationDataPaths(Path.Combine("root", "syltr"));

        Assert.EndsWith(Path.Combine("root", "syltr", "config", "services.json"), paths.ServicesFile);
        Assert.EndsWith(Path.Combine("root", "syltr", "config", "settings.json"), paths.SettingsFile);
        Assert.EndsWith(Path.Combine("root", "syltr", "webview"), paths.WebViewDirectory);
        Assert.EndsWith(Path.Combine("root", "syltr", "logs"), paths.LogsDirectory);
        Assert.EndsWith(Path.Combine("root", "syltr", "migrations", "schema.json"), paths.SchemaFile);
    }

    [Fact]
    public void CurrentUserPathUsesWindowsLocalApplicationData()
    {
        var paths = ApplicationDataPaths.ForCurrentUser();

        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            paths.RootDirectory,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Syltr", paths.RootDirectory);
    }
}
