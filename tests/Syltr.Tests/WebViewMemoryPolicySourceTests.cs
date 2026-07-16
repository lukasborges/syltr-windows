namespace Syltr.Tests;

public sealed class WebViewMemoryPolicySourceTests
{
    [Fact]
    public void Background_views_request_low_memory_and_foreground_view_requests_normal_memory()
    {
        var root = FindRepositoryRoot();
        var hostSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Engine",
            "ServiceViewHost.cs"));
        var pageSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Syltr",
            "Window",
            "MainPage.Services.cs"));

        Assert.Contains("ApplyMemoryUsageTarget();", hostSource, StringComparison.Ordinal);
        Assert.Contains("CoreWebView2MemoryUsageTargetLevel.Normal", hostSource, StringComparison.Ordinal);
        Assert.Contains("CoreWebView2MemoryUsageTargetLevel.Low", hostSource, StringComparison.Ordinal);
        Assert.Contains("host.SetForeground(ReferenceEquals(host, foregroundHost));", pageSource, StringComparison.Ordinal);
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
