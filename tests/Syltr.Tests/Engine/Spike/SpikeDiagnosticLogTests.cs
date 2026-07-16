using Syltr.Engine.Spike;
using Syltr.Tests.Config;

namespace Syltr.Tests.Engine.Spike;

public class SpikeDiagnosticLogTests
{
    [Fact]
    public async Task WritesFailureAndClearsItAfterRecovery()
    {
        using var data = new TemporaryApplicationData();

        await SpikeDiagnosticLog.WriteFailureAsync(
            data.Paths,
            new InvalidOperationException("test failure"));

        var path = SpikeDiagnosticLog.GetPath(data.Paths);
        Assert.True(File.Exists(path));
        Assert.Contains("test failure", await File.ReadAllTextAsync(path));

        SpikeDiagnosticLog.Clear(data.Paths);

        Assert.False(File.Exists(path));
    }
}
