using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class WebViewMemorySnapshotTests
{
    [Fact]
    public void Totals_memory_from_all_captured_processes()
    {
        var snapshot = new WebViewMemorySnapshot(
            DateTimeOffset.UnixEpoch,
            [
                new WebViewProcessMemory(1, "Browser", 100, 80),
                new WebViewProcessMemory(2, "Renderer", 200, 150)
            ]);

        Assert.Equal(300, snapshot.TotalWorkingSetBytes);
        Assert.Equal(230, snapshot.TotalPrivateMemoryBytes);
    }
}
