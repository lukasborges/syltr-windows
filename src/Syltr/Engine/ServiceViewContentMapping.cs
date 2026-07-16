namespace Syltr.Engine;

/// <summary>
/// Maps a HTTPS virtual host to local, read-only application content.
/// </summary>
public sealed record ServiceViewContentMapping(string HostName, string FolderPath);
