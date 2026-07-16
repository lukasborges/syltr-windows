using Syltr.Config;
using Syltr.Engine;
using Syltr.Icon;

namespace Syltr.Tests.Icon;

public sealed class ServiceTileStateTests
{
    [Fact]
    public void Uses_initial_until_a_favicon_is_available()
    {
        var tile = new ServiceTileState(CreateService("Teams"));

        Assert.Equal("T", tile.Initial);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, tile.InitialVisibility);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, tile.IconVisibility);
    }

    [Theory]
    [InlineData(0, "", Microsoft.UI.Xaml.Visibility.Collapsed)]
    [InlineData(7, "7", Microsoft.UI.Xaml.Visibility.Visible)]
    [InlineData(120, "99+", Microsoft.UI.Xaml.Visibility.Visible)]
    public void Formats_unread_badges(
        uint unread,
        string expectedText,
        Microsoft.UI.Xaml.Visibility expectedVisibility)
    {
        var tile = new ServiceTileState(CreateService("Gmail"));

        tile.UpdateState(new ServiceViewState(
            ServiceViewStatus.Ready,
            "Gmail",
            unread,
            false,
            false));

        Assert.Equal(expectedText, tile.UnreadText);
        Assert.Equal(expectedVisibility, tile.UnreadVisibility);
    }

    [Fact]
    public void Visually_dims_disabled_services()
    {
        var tile = new ServiceTileState(CreateService("Slack") with { Disabled = true });

        Assert.Equal(0.45, tile.Opacity);
    }

    [Theory]
    [InlineData(8, 16)]
    [InlineData(20, 20)]
    [InlineData(64, 24)]
    public void Clamps_favicon_to_the_linux_reference_range(double requested, double expected)
    {
        var tile = new ServiceTileState(CreateService("Slack"))
        {
            FaviconSize = requested
        };

        Assert.Equal(expected, tile.FaviconSize);
    }

    private static ServiceDefinition CreateService(string name) => new()
    {
        Id = name.ToLowerInvariant(),
        Name = name,
        Url = "https://example.com"
    };
}
