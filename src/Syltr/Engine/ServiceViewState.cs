namespace Syltr.Engine;

public enum ServiceViewStatus
{
    Created,
    Initializing,
    Ready,
    Navigating,
    Recovering,
    Failed,
    Disabled,
    Closed
}

public enum ServiceViewRecoveryAction
{
    None,
    Reload,
    Recreate
}

public sealed record ServiceViewState(
    ServiceViewStatus Status,
    string DocumentTitle,
    uint UnreadCount,
    bool CanGoBack,
    bool CanGoForward,
    string? ErrorMessage = null,
    ServiceViewRecoveryAction RecoveryAction = ServiceViewRecoveryAction.None);

public sealed class ServiceViewStateChangedEventArgs(ServiceViewState state) : EventArgs
{
    public ServiceViewState State { get; } = state;
}
