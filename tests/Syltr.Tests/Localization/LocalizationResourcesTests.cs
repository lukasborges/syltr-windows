using System.Xml.Linq;

namespace Syltr.Tests.Localization;

public sealed class LocalizationResourcesTests
{
    [Fact]
    public void Portuguese_and_English_resources_have_the_same_keys()
    {
        var root = FindRepositoryRoot();
        var portuguese = LoadKeys(Path.Combine(root, "src", "Syltr", "Strings", "pt-BR", "Resources.resw"));
        var english = LoadKeys(Path.Combine(root, "src", "Syltr", "Strings", "en-US", "Resources.resw"));

        Assert.Equal(portuguese, english);
    }

    [Fact]
    public void Package_declares_every_localized_language()
    {
        var root = FindRepositoryRoot();
        var manifest = XDocument.Load(Path.Combine(root, "src", "Syltr", "Package.appxmanifest"));
        XNamespace package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var languages = manifest
            .Descendants(package + "Resource")
            .Select(resource => resource.Attribute("Language")?.Value)
            .Where(language => language is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("pt-BR", languages);
        Assert.Contains("en-US", languages);
    }

    private static string[] LoadKeys(string path) => XDocument.Load(path)
        .Root!
        .Elements("data")
        .Select(element => element.Attribute("name")?.Value)
        .Where(name => name is not null)
        .Cast<string>()
        .Order(StringComparer.Ordinal)
        .ToArray();

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
