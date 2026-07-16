using Microsoft.UI.Xaml.Controls;
using Syltr.Config.Downloads;
using Syltr.Engine;
using Syltr.Localization;

namespace Syltr.Window;

internal sealed class DownloadCoordinator
{
    private readonly Func<string> _downloadsFolderProvider;
    private readonly HashSet<string> _reservedPaths = new(StringComparer.OrdinalIgnoreCase);

    public DownloadCoordinator(Func<string>? downloadsFolderProvider = null)
    {
        _downloadsFolderProvider = downloadsFolderProvider ?? WindowsDownloadsFolder.GetPath;
    }

    public DownloadPresentation Start(ServiceDownloadRequestedEventArgs request)
    {
        try
        {
            var downloadsFolder = _downloadsFolderProvider();
            Directory.CreateDirectory(downloadsFolder);
            var destination = DownloadPathResolver.CreateUniquePath(
                downloadsFolder,
                request.SuggestedFileName,
                _reservedPaths);
            _reservedPaths.Add(destination);
            request.SaveTo(destination);
            return new DownloadPresentation(
                AppText.Format("Download_InProgressTitle", Path.GetFileName(destination)),
                AppText.Format("Download_SourceMessage", request.ProfileName, request.SourceOrigin.Host),
                InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            request.Cancel();
            return new DownloadPresentation(
                AppText.Get("Download_CancelledTitle"),
                exception.Message,
                InfoBarSeverity.Error);
        }
    }

    public DownloadPresentation Update(ServiceDownloadState state)
    {
        if (state.Status is ServiceDownloadStatus.Completed or ServiceDownloadStatus.Interrupted)
        {
            _reservedPaths.Remove(state.DestinationPath);
        }

        return state.Status switch
        {
            ServiceDownloadStatus.Completed => new DownloadPresentation(
                AppText.Format("Download_CompletedTitle", state.FileName),
                state.DestinationPath,
                InfoBarSeverity.Success,
                ShouldNotify: true),
            ServiceDownloadStatus.Interrupted => new DownloadPresentation(
                AppText.Format("Download_InterruptedTitle", state.FileName),
                state.ErrorMessage ?? AppText.Get("Download_InterruptedMessage"),
                InfoBarSeverity.Error),
            _ => new DownloadPresentation(
                AppText.Format("Download_InProgressTitle", state.FileName),
                FormatProgress(state),
                InfoBarSeverity.Informational)
        };
    }

    public void Clear() => _reservedPaths.Clear();

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var unit = 0;
        var displayValue = (double)Math.Max(0, bytes);
        while (displayValue >= 1024 && unit < units.Length - 1)
        {
            displayValue /= 1024;
            unit++;
        }

        return $"{displayValue:0.#} {units[unit]}";
    }

    private static string FormatProgress(ServiceDownloadState state)
    {
        var received = FormatBytes(state.BytesReceived);
        return state.TotalBytes is > 0
            ? AppText.Format(
                "Download_ProgressKnown",
                received,
                FormatBytes(state.TotalBytes.Value),
                state.ProfileName)
            : AppText.Format("Download_ProgressUnknown", received, state.ProfileName);
    }
}

internal sealed record DownloadPresentation(
    string Title,
    string Message,
    InfoBarSeverity Severity,
    bool ShouldNotify = false);
