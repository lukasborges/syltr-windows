namespace Syltr.Catalog;

/// <summary>
/// Describes a known service offered as a shortcut when adding a service.
/// </summary>
public sealed record ServiceCatalogEntry(
    string Key,
    string Name,
    string Url,
    ServiceCatalogCategory Category);
