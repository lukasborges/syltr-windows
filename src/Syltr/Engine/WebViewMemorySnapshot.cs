namespace Syltr.Engine;

public sealed record WebViewProcessMemory(
    int ProcessId,
    string Kind,
    long WorkingSetBytes,
    long PrivateMemoryBytes);

public sealed record WebViewMemorySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<WebViewProcessMemory> Processes)
{
    public long TotalWorkingSetBytes => Processes.Sum(process => process.WorkingSetBytes);

    public long TotalPrivateMemoryBytes => Processes.Sum(process => process.PrivateMemoryBytes);
}
