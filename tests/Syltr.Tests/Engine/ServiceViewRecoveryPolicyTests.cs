using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ServiceViewRecoveryPolicyTests
{
    [Theory]
    [InlineData(ServiceViewProcessFailureKind.BrowserExited, ServiceViewRecoveryAction.Recreate)]
    [InlineData(ServiceViewProcessFailureKind.RendererExited, ServiceViewRecoveryAction.Reload)]
    [InlineData(ServiceViewProcessFailureKind.RendererUnresponsive, ServiceViewRecoveryAction.Reload)]
    [InlineData(ServiceViewProcessFailureKind.NonFatal, ServiceViewRecoveryAction.None)]
    public void Selects_the_recovery_required_by_the_failed_process(
        ServiceViewProcessFailureKind failure,
        ServiceViewRecoveryAction expected)
    {
        Assert.Equal(expected, ServiceViewRecoveryPolicy.ForProcessFailure(failure));
    }
}
