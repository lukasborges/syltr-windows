namespace Syltr.Engine;

public enum ServiceDownloadDecision
{
    Save,
    Cancel
}

public sealed record ServiceDownloadResponse(
    ServiceDownloadDecision Decision,
    string? DestinationPath);

/// <summary>
/// A download request without WebView2-specific types or sensitive URL details.
/// </summary>
public sealed class ServiceDownloadRequestedEventArgs : EventArgs
{
    private readonly TaskCompletionSource<ServiceDownloadResponse> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _decisionLock = new();

    public ServiceDownloadRequestedEventArgs(
        string profileName,
        Uri sourceOrigin,
        string suggestedFileName,
        string mimeType,
        long? totalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(sourceOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        ProfileName = profileName;
        SourceOrigin = new UriBuilder(
            sourceOrigin.Scheme,
            sourceOrigin.IdnHost,
            sourceOrigin.IsDefaultPort ? -1 : sourceOrigin.Port).Uri;
        SuggestedFileName = suggestedFileName;
        MimeType = mimeType ?? string.Empty;
        TotalBytes = totalBytes is > 0 ? totalBytes : null;
    }

    public string ProfileName { get; }

    public Uri SourceOrigin { get; }

    public string SuggestedFileName { get; }

    public string MimeType { get; }

    public long? TotalBytes { get; }

    public ServiceDownloadResponse? Response { get; private set; }

    public bool IsDecided => Response is not null;

    public bool SaveTo(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException("Download destination must be fully qualified.", nameof(destinationPath));
        }

        return Decide(new ServiceDownloadResponse(
            ServiceDownloadDecision.Save,
            Path.GetFullPath(destinationPath)));
    }

    public bool Cancel() => Decide(new ServiceDownloadResponse(ServiceDownloadDecision.Cancel, null));

    internal Task<ServiceDownloadResponse> WaitForDecisionAsync() => _completion.Task;

    private bool Decide(ServiceDownloadResponse response)
    {
        lock (_decisionLock)
        {
            if (Response is not null)
            {
                return false;
            }

            Response = response;
            _completion.SetResult(response);
            return true;
        }
    }
}
