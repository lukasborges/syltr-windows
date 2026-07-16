using System.Text.Json;
using Syltr.Config;

namespace Syltr.Engine;

public sealed class WebConsoleDiagnosticLog(ApplicationDataPaths paths)
{
    private const string FileName = "web-console.jsonl";
    private const long MaximumFileBytes = 2 * 1024 * 1024;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public string Path { get; } = GetPath(paths);

    public static string GetPath(ApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return System.IO.Path.Combine(paths.LogsDirectory, FileName);
    }

    public async Task AppendAsync(
        ServiceConsoleMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path)!;
                Directory.CreateDirectory(directory);
                RotateIfNeeded();
                var entry = JsonSerializer.Serialize(new
                {
                    timestampUtc = message.TimestampUtc,
                    profile = message.ProfileName,
                    level = message.Level.ToString().ToLowerInvariant(),
                    message = message.Message,
                    sourceOrigin = message.SourceOrigin?.ToString(),
                    line = message.LineNumber,
                    column = message.ColumnNumber
                });
                await File.AppendAllTextAsync(
                    Path,
                    entry + Environment.NewLine,
                    cancellationToken);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Optional diagnostics must never affect the hosted service.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(Path) || new FileInfo(Path).Length < MaximumFileBytes)
        {
            return;
        }

        File.Move(Path, Path + ".1", overwrite: true);
    }
}
