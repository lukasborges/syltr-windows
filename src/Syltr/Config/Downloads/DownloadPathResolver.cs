namespace Syltr.Config.Downloads;

/// <summary>
/// Produces safe, collision-free paths for files saved by untrusted web content.
/// </summary>
public static class DownloadPathResolver
{
    private const int MaximumFileNameLength = 180;

    public static string CreateUniquePath(
        string downloadsDirectory,
        string? suggestedFileName,
        IReadOnlySet<string>? reservedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadsDirectory);
        var directory = Path.GetFullPath(downloadsDirectory);
        var fileName = SanitizeFileName(suggestedFileName);
        var candidate = Path.Combine(directory, fileName);
        if (IsAvailable(candidate, reservedPaths))
        {
            return candidate;
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 20)
        {
            extension = string.Empty;
        }
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (extension.Length == 0)
        {
            stem = fileName;
        }
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var suffixText = $" ({suffix})";
            var maximumStemLength = MaximumFileNameLength - extension.Length - suffixText.Length;
            var candidateStem = stem.Length > maximumStemLength
                ? stem[..maximumStemLength]
                : stem;
            candidate = Path.Combine(directory, $"{candidateStem}{suffixText}{extension}");
            if (IsAvailable(candidate, reservedPaths))
            {
                return candidate;
            }
        }

        throw new IOException("A collision-free download path could not be created.");
    }

    private static bool IsAvailable(string path, IReadOnlySet<string>? reservedPaths) =>
        !Path.Exists(path) &&
        (reservedPaths is null || !reservedPaths.Contains(path));

    public static string SanitizeFileName(string? suggestedFileName)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "download"
            : Path.GetFileName(suggestedFileName.Trim());
        var invalidCharacters = Path.GetInvalidFileNameChars();
        fileName = string.Concat(fileName.Select(character =>
            invalidCharacters.Contains(character) || char.IsControl(character)
                ? '_'
                : character));
        fileName = fileName.Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 20)
        {
            extension = string.Empty;
        }

        var stem = extension.Length == 0
            ? fileName
            : Path.GetFileNameWithoutExtension(fileName);
        if (IsReservedWindowsName(stem))
        {
            stem = $"_{stem}";
            fileName = $"{stem}{extension}";
        }

        if (fileName.Length <= MaximumFileNameLength)
        {
            return fileName;
        }

        var maximumStemLength = Math.Max(1, MaximumFileNameLength - extension.Length);
        return $"{stem[..Math.Min(stem.Length, maximumStemLength)]}{extension}";
    }

    private static bool IsReservedWindowsName(string stem)
    {
        var name = stem.TrimEnd(' ', '.');
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               IsNumberedDevice(name, "COM") ||
               IsNumberedDevice(name, "LPT");
    }

    private static bool IsNumberedDevice(string name, string prefix) =>
        name.Length == 4 &&
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        name[3] is >= '1' and <= '9';
}
