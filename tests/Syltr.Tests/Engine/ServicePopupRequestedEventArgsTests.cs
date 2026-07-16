using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class ServicePopupRequestedEventArgsTests
{
    [Fact]
    public void Exposes_only_the_origin_for_native_display()
    {
        var request = CreateRequest(
            new Uri("https://user:secret@example.com/private?token=secret"),
            isSameOrigin: false);

        Assert.Equal("https://example.com/", request.RequestedOrigin?.AbsoluteUri);
    }

    [Fact]
    public void Only_the_first_popup_disposition_is_accepted()
    {
        var request = CreateRequest(new Uri("https://example.com"), isSameOrigin: true);

        Assert.True(request.OpenInApp());
        Assert.False(request.OpenExternal());
        Assert.Equal(ServicePopupDisposition.InApp, request.Disposition);
    }

    private static ServicePopupRequestedEventArgs CreateRequest(Uri uri, bool isSameOrigin)
    {
        var host = (ServiceViewHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(ServiceViewHost));
        return new ServicePopupRequestedEventArgs(host, uri, true, isSameOrigin);
    }
}
