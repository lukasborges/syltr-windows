namespace Syltr.Engine;

public sealed record ServiceNotification(
    string ProfileName,
    Uri SenderOrigin,
    string Title,
    string Body,
    string Tag,
    bool IsSilent,
    bool RequiresInteraction);

public enum ServiceNotificationInteraction
{
    None,
    Clicked,
    Closed
}

public sealed class ServiceNotificationReceivedEventArgs : EventArgs
{
    private readonly Action? _reportClicked;
    private readonly Action? _reportClosed;
    private readonly object _interactionLock = new();

    public ServiceNotificationReceivedEventArgs(ServiceNotification notification)
        : this(notification, null, null)
    {
    }

    internal ServiceNotificationReceivedEventArgs(
        ServiceNotification notification,
        Action? reportClicked,
        Action? reportClosed)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Notification = notification;
        _reportClicked = reportClicked;
        _reportClosed = reportClosed;
    }

    public ServiceNotification Notification { get; }

    public ServiceNotificationInteraction Interaction { get; private set; }

    public bool Click() => Interact(ServiceNotificationInteraction.Clicked, _reportClicked);

    public bool Close() => Interact(ServiceNotificationInteraction.Closed, _reportClosed);

    private bool Interact(ServiceNotificationInteraction interaction, Action? callback)
    {
        lock (_interactionLock)
        {
            if (Interaction != ServiceNotificationInteraction.None)
            {
                return false;
            }

            Interaction = interaction;
        }

        try
        {
            callback?.Invoke();
        }
        catch
        {
            // The WebView may have closed after the native notification was shown.
        }

        return true;
    }
}
