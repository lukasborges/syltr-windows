namespace Syltr.Engine;

/// <summary>
/// Browser capabilities that a service may request from its isolated profile.
/// </summary>
public enum ServicePermissionKind
{
    Unknown,
    Microphone,
    Camera,
    Geolocation,
    Notifications,
    OtherSensors,
    ClipboardRead,
    MultipleAutomaticDownloads,
    FileReadWrite,
    Autoplay,
    LocalFonts,
    MidiSystemExclusiveMessages,
    WindowManagement
}
