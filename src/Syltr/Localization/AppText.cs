using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Syltr.Localization;

internal static class AppText
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string resourceId) => Loader.GetString(resourceId);

    public static string Format(string resourceId, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(resourceId), arguments);
}
