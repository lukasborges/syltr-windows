namespace Syltr.Engine;

public enum ServiceDownloadStatus
{
    InProgress,
    Completed,
    Interrupted
}

public sealed record ServiceDownloadState(
    Guid Id,
    string ProfileName,
    string FileName,
    string DestinationPath,
    long BytesReceived,
    long? TotalBytes,
    ServiceDownloadStatus Status,
    string? ErrorMessage = null);

public sealed class ServiceDownloadStateChangedEventArgs(ServiceDownloadState state) : EventArgs
{
    public ServiceDownloadState State { get; } = state;
}
