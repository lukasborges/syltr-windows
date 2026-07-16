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

    [Fact]
    public void Appinstaller_generator_targets_stable_github_release_assets()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "new-appinstaller.ps1"));

        Assert.Contains("releases/latest/download", script, StringComparison.Ordinal);
        Assert.Contains("Syltr-$architecture.msix", script, StringComparison.Ordinal);
        Assert.Contains("appinstaller/2017/2", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsigned_release_candidate_cannot_publish_repository_contents()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "release-candidate.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v7", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action-gh-release", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Code_signing_policy_declares_required_roles_and_privacy_policy()
    {
        var root = FindRepositoryRoot();
        var policy = File.ReadAllText(Path.Combine(root, "CODE_SIGNING_POLICY.md"));

        Assert.Contains(
            "Free code signing provided by SignPath.io, certificate by SignPath Foundation",
            policy,
            StringComparison.Ordinal);
        Assert.Contains("Committers and reviewers:", policy, StringComparison.Ordinal);
        Assert.Contains("Approvers:", policy, StringComparison.Ordinal);
        Assert.Contains("PRIVACY.md", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_installer_uses_a_non_exportable_machine_trusted_certificate()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "install-local-msix.ps1"));

        Assert.Contains("-KeyExportPolicy NonExportable", script, StringComparison.Ordinal);
        Assert.Contains("StoreLocation]::LocalMachine", script, StringComparison.Ordinal);
        Assert.Contains("StoreName]::TrustedPeople", script, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", script, StringComparison.Ordinal);
        Assert.DoesNotContain("TrustedRoot", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Package_icons_are_generated_from_the_syltr_artwork()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "Syltr", "Syltr.csproj"));
        var generator = File.ReadAllText(Path.Combine(root, "scripts", "generate-app-assets.ps1"));
        var iconPath = Path.Combine(
            root,
            "src",
            "Syltr",
            "Assets",
            "Square44x44Logo.targetsize-24_altform-unplated.png");
        const string templateHash =
            "E7AA3D860CE3D95E7EC17F82C65295BACE2A09B56F0B94290B635F65375CEF87";

        Assert.Contains("<ApplicationIcon>Assets\\AppIcon.ico</ApplicationIcon>", project);
        Assert.Contains("Syltr.svg", generator, StringComparison.Ordinal);
        Assert.NotEqual(
            templateHash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(iconPath))));
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
