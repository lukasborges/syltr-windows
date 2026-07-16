namespace Syltr.Engine;

/// <summary>
/// A permission request reduced to the information needed by the native UI.
/// </summary>
public sealed class ServicePermissionRequestedEventArgs : EventArgs
{
    private readonly TaskCompletionSource<ServicePermissionResponse> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _decisionLock = new();

    public ServicePermissionRequestedEventArgs(
        string profileName,
        Uri origin,
        ServicePermissionKind kind,
        bool isUserInitiated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri)
        {
            throw new ArgumentException("Permission origin must be absolute.", nameof(origin));
        }

        ProfileName = profileName;
        Origin = new UriBuilder(origin.Scheme, origin.IdnHost, origin.IsDefaultPort ? -1 : origin.Port).Uri;
        Kind = kind;
        IsUserInitiated = isUserInitiated;
    }

    public string ProfileName { get; }

    /// <summary>
    /// Contains only scheme and authority; paths, query strings and fragments are discarded.
    /// </summary>
    public Uri Origin { get; }

    public ServicePermissionKind Kind { get; }

    public bool IsUserInitiated { get; }

    public ServicePermissionResponse? Response { get; private set; }

    public bool IsDecided => Response is not null;

    public bool Allow(bool rememberForProfile = true) =>
        Decide(ServicePermissionDecision.Allow, rememberForProfile);

    public bool Deny(bool rememberForProfile = true) =>
        Decide(ServicePermissionDecision.Deny, rememberForProfile);

    internal Task<ServicePermissionResponse> WaitForDecisionAsync() => _completion.Task;

    private bool Decide(ServicePermissionDecision decision, bool rememberForProfile)
    {
        lock (_decisionLock)
        {
            if (Response is not null)
            {
                return false;
            }

            Response = new ServicePermissionResponse(decision, rememberForProfile);
            _completion.SetResult(Response);
            return true;
        }
    }
}
