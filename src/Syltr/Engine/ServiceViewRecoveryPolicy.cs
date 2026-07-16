namespace Syltr.Engine;

public enum ServiceViewProcessFailureKind
{
    BrowserExited,
    RendererExited,
    RendererUnresponsive,
    NonFatal
}

public static class ServiceViewRecoveryPolicy
{
    public static ServiceViewRecoveryAction ForProcessFailure(ServiceViewProcessFailureKind failure) =>
        failure switch
        {
            ServiceViewProcessFailureKind.BrowserExited => ServiceViewRecoveryAction.Recreate,
            ServiceViewProcessFailureKind.RendererExited => ServiceViewRecoveryAction.Reload,
            ServiceViewProcessFailureKind.RendererUnresponsive => ServiceViewRecoveryAction.Reload,
            _ => ServiceViewRecoveryAction.None
        };
}
