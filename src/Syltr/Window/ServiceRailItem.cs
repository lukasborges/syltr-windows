using Syltr.Config;
using Syltr.Engine;
using Syltr.Icon;

namespace Syltr.Window;

internal sealed class ServiceRailItem
{
    public ServiceRailItem(
        ServiceDefinition service,
        ServiceViewHost host,
        Microsoft.UI.Xaml.FrameworkElement content)
    {
        Host = host;
        Content = content;
        Tile = new ServiceTileState(service);
    }

    public ServiceViewHost Host { get; }

    public Microsoft.UI.Xaml.FrameworkElement Content { get; set; }

    public ServiceTileState Tile { get; }

    public uint UnreadCount { get; private set; }

    public void UpdateService(ServiceDefinition service) => Tile.UpdateService(service);

    public void UpdateState(ServiceViewState state)
    {
        UnreadCount = state.UnreadCount;
        Tile.UpdateState(state);
    }
}
