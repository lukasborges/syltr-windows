using Microsoft.Web.WebView2.Core;

namespace Syltr.Engine;

internal static class WebViewRequestPolicy
{
    private static readonly Uri UnknownOrigin = new("https://unknown.invalid");

    public static Uri CreateSafeOrigin(string requestedUri)
    {
        if (Uri.TryCreate(requestedUri, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            return new Uri(uri.GetLeftPart(UriPartial.Authority));
        }

        return UnknownOrigin;
    }

    public static bool HasSameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    public static string? NormalizeUserAgent(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();

    public static ServicePermissionKind MapPermissionKind(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Microphone => ServicePermissionKind.Microphone,
        CoreWebView2PermissionKind.Camera => ServicePermissionKind.Camera,
        CoreWebView2PermissionKind.Geolocation => ServicePermissionKind.Geolocation,
        CoreWebView2PermissionKind.Notifications => ServicePermissionKind.Notifications,
        CoreWebView2PermissionKind.OtherSensors => ServicePermissionKind.OtherSensors,
        CoreWebView2PermissionKind.ClipboardRead => ServicePermissionKind.ClipboardRead,
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => ServicePermissionKind.MultipleAutomaticDownloads,
        CoreWebView2PermissionKind.FileReadWrite => ServicePermissionKind.FileReadWrite,
        CoreWebView2PermissionKind.Autoplay => ServicePermissionKind.Autoplay,
        CoreWebView2PermissionKind.LocalFonts => ServicePermissionKind.LocalFonts,
        CoreWebView2PermissionKind.MidiSystemExclusiveMessages => ServicePermissionKind.MidiSystemExclusiveMessages,
        CoreWebView2PermissionKind.WindowManagement => ServicePermissionKind.WindowManagement,
        _ => ServicePermissionKind.Unknown
    };
}
