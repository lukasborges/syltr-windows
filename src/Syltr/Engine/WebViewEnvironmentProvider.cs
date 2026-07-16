using Microsoft.Web.WebView2.Core;
using Syltr.Config;
using System.Diagnostics;

namespace Syltr.Engine;

/// <summary>
/// Owns the shared WebView2 environment used by every service profile.
/// </summary>
public sealed class WebViewEnvironmentProvider
{
    private readonly Lazy<Task<CoreWebView2Environment>> _environment;

    public WebViewEnvironmentProvider(ApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        UserDataDirectory = paths.WebViewDirectory;
        _environment = new Lazy<Task<CoreWebView2Environment>>(
            CreateEnvironmentAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string UserDataDirectory { get; }

    internal Task<CoreWebView2Environment> GetAsync() => _environment.Value;

    public async Task<WebViewMemorySnapshot> CaptureMemorySnapshotAsync()
    {
        var environment = await GetAsync();
        var processes = new List<WebViewProcessMemory>();
        foreach (var processInfo in environment.GetProcessInfos().DistinctBy(info => info.ProcessId))
        {
            try
            {
                using var process = Process.GetProcessById(processInfo.ProcessId);
                process.Refresh();
                processes.Add(new WebViewProcessMemory(
                    processInfo.ProcessId,
                    processInfo.Kind.ToString(),
                    process.WorkingSet64,
                    process.PrivateMemorySize64));
            }
            catch (ArgumentException)
            {
                // Chromium processes can exit between enumeration and inspection.
            }
            catch (InvalidOperationException)
            {
                // Treat a process that exits during inspection as absent.
            }
        }

        return new WebViewMemorySnapshot(DateTimeOffset.Now, processes);
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(UserDataDirectory);
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, UserDataDirectory, null);
    }
}
