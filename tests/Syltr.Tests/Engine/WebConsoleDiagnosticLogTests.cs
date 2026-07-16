using System.Text.Json;
using Syltr.Engine;
using Syltr.Tests.Config;

namespace Syltr.Tests.Engine;

public sealed class WebConsoleDiagnosticLogTests
{
    [Fact]
    public async Task Writes_structured_json_lines()
    {
        using var data = new TemporaryApplicationData();
        var log = new WebConsoleDiagnosticLog(data.Paths);
        var message = new ServiceConsoleMessage(
            DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
            "gmail-work",
            ServiceConsoleMessageLevel.Warning,
            "deprecated API",
            new Uri("https://mail.google.com/"),
            12,
            7);

        await log.AppendAsync(message);

        using var entry = JsonDocument.Parse((await File.ReadAllTextAsync(log.Path)).Trim());
        Assert.Equal("gmail-work", entry.RootElement.GetProperty("profile").GetString());
        Assert.Equal("warning", entry.RootElement.GetProperty("level").GetString());
        Assert.Equal("https://mail.google.com/", entry.RootElement.GetProperty("sourceOrigin").GetString());
    }

    [Fact]
    public async Task Rotates_the_log_after_the_size_limit()
    {
        using var data = new TemporaryApplicationData();
        var log = new WebConsoleDiagnosticLog(data.Paths);
        Directory.CreateDirectory(data.Paths.LogsDirectory);
        await File.WriteAllBytesAsync(log.Path, new byte[(2 * 1024 * 1024) + 1]);

        await log.AppendAsync(new ServiceConsoleMessage(
            DateTimeOffset.UtcNow,
            "profile",
            ServiceConsoleMessageLevel.Info,
            "new entry",
            null,
            null,
            null));

        Assert.True(File.Exists(log.Path + ".1"));
        Assert.Contains("new entry", await File.ReadAllTextAsync(log.Path));
    }
}
