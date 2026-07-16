namespace Syltr.Engine;

public enum ServicePermissionDecision
{
    Allow,
    Deny
}

public sealed record ServicePermissionResponse(
    ServicePermissionDecision Decision,
    bool RememberForProfile);
