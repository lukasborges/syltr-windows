namespace Syltr.Config;

/// <summary>
/// Generates stable profile-safe identifiers for service instances.
/// </summary>
public static class ServiceIdGenerator
{
    public static string CreateUnique(IEnumerable<ServiceDefinition> existing, string baseName)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(baseName);

        var existingIds = existing
            .Select(service => service.Id)
            .ToHashSet(StringComparer.Ordinal);
        var slug = Slugify(baseName);

        if (!existingIds.Contains(slug))
        {
            return slug;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No identifier is available for service '{baseName}'.");
    }

    private static string Slugify(string baseName)
    {
        var characters = baseName
            .ToLowerInvariant()
            .Select(character => character is >= 'a' and <= 'z' or >= '0' and <= '9'
                ? character
                : '-');
        var slug = new string([.. characters]).Trim('-');

        return slug.Length == 0 ? "service" : slug;
    }
}
