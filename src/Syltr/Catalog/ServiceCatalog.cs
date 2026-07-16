using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Syltr.Config;

namespace Syltr.Catalog;

/// <summary>
/// Provides the built-in recipes for web services with stable URLs.
/// </summary>
public static class ServiceCatalog
{
    private const string ResourceName = "Syltr.Catalog.Services.json";

    private static readonly IReadOnlyList<ServiceCatalogEntry> CatalogEntries = LoadEntries();
    private static readonly IReadOnlyList<ServiceCatalogCategory> CatalogCategories =
        Array.AsReadOnly([.. CatalogEntries.Select(entry => entry.Category).Distinct()]);

    public static IReadOnlyList<ServiceCatalogEntry> Entries => CatalogEntries;

    public static IReadOnlyList<ServiceCatalogCategory> Categories => CatalogCategories;

    public static ServiceCatalogEntry? FindByKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return CatalogEntries.FirstOrDefault(entry =>
            string.Equals(entry.Key, key, StringComparison.Ordinal));
    }

    private static IReadOnlyList<ServiceCatalogEntry> LoadEntries()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded catalog resource '{ResourceName}' was not found.");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter<ServiceCatalogCategory>() }
        };
        var entries = JsonSerializer.Deserialize<ServiceCatalogEntry[]>(stream, options)
            ?? throw new InvalidDataException("The embedded service catalog is empty.");

        Validate(entries);
        return new ReadOnlyCollection<ServiceCatalogEntry>(entries);
    }

    private static void Validate(IReadOnlyCollection<ServiceCatalogEntry> entries)
    {
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The embedded service catalog contains no entries.");
        }

        var duplicateKey = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateKey is not null)
        {
            throw new InvalidDataException($"The embedded service catalog contains duplicate key '{duplicateKey}'.");
        }

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key)
                || entry.Key.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9')))
            {
                throw new InvalidDataException($"Catalog key '{entry.Key}' is not lowercase ASCII-alphanumeric.");
            }

            if (string.IsNullOrWhiteSpace(entry.Name)
                || !ServiceUrl.TryNormalize(entry.Url, out var normalized)
                || !string.Equals(entry.Url, normalized, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Catalog entry '{entry.Key}' is invalid.");
            }
        }
    }
}
