using Syltr.Window;

namespace Syltr.Tests.Window;

public sealed class DownloadCoordinatorTests
{
    [Theory]
    [InlineData(-1, 0, "B")]
    [InlineData(0, 0, "B")]
    [InlineData(1023, 1023, "B")]
    [InlineData(1024, 1, "KB")]
    [InlineData(1536, 1.5, "KB")]
    [InlineData(1048576, 1, "MB")]
    [InlineData(1073741824, 1, "GB")]
    public void FormatBytesUsesReadableBinaryUnits(long bytes, double value, string unit)
    {
        var expected = $"{value:0.#} {unit}";
        Assert.Equal(expected, DownloadCoordinator.FormatBytes(bytes));
    }
}
