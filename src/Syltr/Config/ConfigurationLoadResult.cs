namespace Syltr.Config;

public enum ConfigurationLoadStatus
{
    Loaded,
    Created,
    Corrupted,
    Unreadable
}

/// <summary>
/// Returns usable configuration together with any recovery condition encountered while loading it.
/// </summary>
public sealed record ConfigurationLoadResult<T>(
    T Value,
    ConfigurationLoadStatus Status,
    Exception? Error = null);
