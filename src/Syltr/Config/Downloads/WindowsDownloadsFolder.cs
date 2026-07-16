using System.Runtime.InteropServices;

namespace Syltr.Config.Downloads;

public static class WindowsDownloadsFolder
{
    private static readonly Guid DownloadsFolderId =
        new("374DE290-123F-4565-9164-39C4925E467B");

    public static string GetPath()
    {
        var folderId = DownloadsFolderId;
        var result = SHGetKnownFolderPath(ref folderId, 0, nint.Zero, out var pathPointer);
        if (result >= 0)
        {
            try
            {
                var path = Marshal.PtrToStringUni(pathPointer);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return Path.GetFullPath(path);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new InvalidOperationException("The Windows Downloads folder is unavailable.");
        }

        return Path.Combine(profile, "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        ref Guid folderId,
        uint flags,
        nint token,
        out nint path);
}
