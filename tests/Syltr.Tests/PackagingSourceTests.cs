using System.Xml.Linq;

namespace Syltr.Tests;

public sealed class PackagingSourceTests
{
    [Fact]
    public void Msix_version_matches_the_application_version()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "Syltr", "Syltr.csproj"));
        var manifest = XDocument.Load(Path.Combine(root, "src", "Syltr", "Package.appxmanifest"));

        var applicationVersion = Version.Parse(
            project.Root!.Elements("PropertyGroup").Elements("Version").Single().Value);
        XNamespace packageNamespace = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var packageVersion = Version.Parse(
            manifest.Root!.Element(packageNamespace + "Identity")!.Attribute("Version")!.Value);

        Assert.Equal(
            new Version(applicationVersion.Major, applicationVersion.Minor, applicationVersion.Build, 0),
            packageVersion);
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
