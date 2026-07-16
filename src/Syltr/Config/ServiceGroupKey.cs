namespace Syltr.Config;

/// <summary>
/// Identifies configured instances that represent the same web service recipe.
/// </summary>
public static class ServiceGroupKey
{
    public static string FromService(ServiceDefinition service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var uri = new Uri(service.Url, UriKind.Absolute);
        var origin = new UriBuilder(
            uri.Scheme.ToLowerInvariant(),
            uri.IdnHost.ToLowerInvariant(),
            uri.IsDefaultPort ? -1 : uri.Port).Uri.GetLeftPart(UriPartial.Authority);
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{origin}{path}";
    }
}
