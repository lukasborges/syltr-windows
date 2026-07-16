namespace Syltr.Engine;

public sealed class ServiceFaviconChangedEventArgs(byte[] pngBytes) : EventArgs
{
    public byte[] PngBytes { get; } = pngBytes ?? throw new ArgumentNullException(nameof(pngBytes));
}
