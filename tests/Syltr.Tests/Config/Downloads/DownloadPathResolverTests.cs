using Syltr.Config.Downloads;

namespace Syltr.Tests.Config.Downloads;

public sealed class DownloadPathResolverTests
{
    [Theory]
    [InlineData(null, "download")]
    [InlineData("", "download")]
    [InlineData("..\\..\\report.txt", "report.txt")]
    [InlineData("bad<name>.txt", "bad_name_.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("LPT9", "_LPT9")]
    public void Sanitizes_untrusted_file_names(string? suggested, string expected)
    {
        Assert.Equal(expected, DownloadPathResolver.SanitizeFileName(suggested));
    }

    [Fact]
    public void Limits_file_name_length()
    {
        var result = DownloadPathResolver.SanitizeFileName($"{new string('a', 300)}.txt");

        Assert.True(result.Length <= 180);
        Assert.EndsWith(".txt", result);
    }

    [Fact]
    public void Adds_a_suffix_without_overwriting_existing_files()
    {
        using var data = new TemporaryApplicationData();
        var downloads = Path.Combine(data.Root, "Downloads");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "report.txt"), "first");
        File.WriteAllText(Path.Combine(downloads, "report (1).txt"), "second");

        var result = DownloadPathResolver.CreateUniquePath(downloads, "report.txt");

        Assert.Equal(Path.Combine(downloads, "report (2).txt"), result);
    }

    [Fact]
    public void Result_stays_inside_the_download_directory()
    {
        using var data = new TemporaryApplicationData();
        var downloads = Path.Combine(data.Root, "Downloads");

        var result = DownloadPathResolver.CreateUniquePath(downloads, "..\\..\\secret.txt");

        Assert.Equal(Path.GetFullPath(downloads), Path.GetDirectoryName(result));
    }

    [Fact]
    public void Avoids_paths_reserved_by_simultaneous_downloads()
    {
        using var data = new TemporaryApplicationData();
        var downloads = Path.Combine(data.Root, "Downloads");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(downloads, "report.txt")
        };

        var result = DownloadPathResolver.CreateUniquePath(downloads, "report.txt", reserved);

        Assert.Equal(Path.Combine(downloads, "report (1).txt"), result);
    }
}
