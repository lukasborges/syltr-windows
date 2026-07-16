using System.Xml.Linq;

namespace Syltr.Tests;

public class ArchitectureTests
{
    [Fact]
    public void SourceTreeMirrorsReferenceModules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src", "Syltr");
        var expectedModules = new[]
        {
            "Catalog",
            "Config",
            "Engine",
            Path.Combine("Engine", "Unread"),
            Path.Combine("Engine", "UserAgent"),
            Path.Combine("Engine", "WebAppScripts"),
            "Icon",
            "Spellcheck",
            "Window"
        };

        Assert.All(expectedModules, module =>
            Assert.True(Directory.Exists(Path.Combine(sourceRoot, module)), $"Missing module: {module}"));

        var project = XDocument.Load(Path.Combine(sourceRoot, "Syltr.csproj"));
        Assert.Equal("Syltr", project.Descendants("RootNamespace").Single().Value);
        Assert.Equal("Syltr", project.Descendants("AssemblyName").Single().Value);

        var appXaml = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml"));
        var windowXaml = File.ReadAllText(Path.Combine(sourceRoot, "Window", "MainWindow.xaml"));
        Assert.Contains("x:Class=\"Syltr.App\"", appXaml);
        Assert.Contains("x:Class=\"Syltr.Window.MainWindow\"", windowXaml);
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
