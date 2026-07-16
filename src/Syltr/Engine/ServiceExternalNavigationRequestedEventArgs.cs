namespace Syltr.Engine;

/// <summary>
/// A user-clicked link that requested a separate browsing context.
/// </summary>
public sealed class ServiceExternalNavigationRequestedEventArgs : EventArgs
{
    public ServiceExternalNavigationRequestedEventArgs(string profileName, Uri destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri)
        {
            throw new ArgumentException("External destination must be absolute.", nameof(destination));
        }

        ProfileName = profileName;
        Destination = destination;
        DestinationOrigin = string.IsNullOrWhiteSpace(destination.Host)
            ? null
            : new UriBuilder(
                destination.Scheme,
                destination.IdnHost,
                destination.IsDefaultPort ? -1 : destination.Port).Uri;
    }

    public string ProfileName { get; }

    public Uri Destination { get; }

    public Uri? DestinationOrigin { get; }
}
