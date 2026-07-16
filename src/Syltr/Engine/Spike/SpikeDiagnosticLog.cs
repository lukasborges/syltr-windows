using System.Text;
using Syltr.Config;

namespace Syltr.Engine.Spike;

public static class SpikeDiagnosticLog
{
    private const string FileName = "webview-isolation-spike.log";

    public static string GetPath(ApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.LogsDirectory, FileName);
    }

    public static async Task WriteFailureAsync(
        ApplicationDataPaths paths,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            var text = new StringBuilder()
                .AppendLine($"Time (UTC): {DateTimeOffset.UtcNow:O}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"Process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}")
                .AppendLine($"Application base: {AppContext.BaseDirectory}")
                .AppendLine($"WebView data: {paths.WebViewDirectory}")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();
            await File.WriteAllTextAsync(GetPath(paths), text);
        }
        catch
        {
            // Diagnostics must never hide the original WebView2 failure.
        }
    }

    public static void Clear(ApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            var path = GetPath(paths);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Stale diagnostics must not block a successful startup.
        }
    }
}
