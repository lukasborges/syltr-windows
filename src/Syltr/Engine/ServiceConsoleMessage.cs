namespace Syltr.Engine;

public enum ServiceConsoleMessageLevel
{
    Log,
    Debug,
    Info,
    Warning,
    Error
}

public sealed record ServiceConsoleMessage(
    DateTimeOffset TimestampUtc,
    string ProfileName,
    ServiceConsoleMessageLevel Level,
    string Message,
    Uri? SourceOrigin,
    int? LineNumber,
    int? ColumnNumber);

public sealed class ServiceConsoleMessageReceivedEventArgs(ServiceConsoleMessage message) : EventArgs
{
    public ServiceConsoleMessage Message { get; } = message;
}
