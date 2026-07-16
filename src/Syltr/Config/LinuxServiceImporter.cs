using System.Text.Json;

namespace Syltr.Config;

/// <summary>
/// Reads the reference Linux services.json schema and plans a non-destructive merge.
/// Browser session data is intentionally outside the import contract.
/// </summary>
public static class LinuxServiceImporter
{
    public static async Task<IReadOnlyList<ServiceDefinition>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var services = await AtomicJsonFile.ReadAsync<List<ServiceDefinition?>>(
            path,
            ConfigurationJson.Options,
            cancellationToken)
            ?? throw new JsonException("The Linux services file contains JSON null.");

        return services
            .Select((service, index) => service is null
                ? throw new JsonException($"Service at index {index} is JSON null.")
                : ValidateAndNormalize(service))
            .ToArray();
    }

    public static LinuxServiceImportPlan CreatePlan(
        IEnumerable<ServiceDefinition> existing,
        IEnumerable<ServiceDefinition> imported)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(imported);

        var reserved = existing.ToList();
        var additions = new List<ServiceDefinition>();
        var skippedDuplicates = 0;
        var reassignedIds = 0;

        foreach (var candidate in imported.Select(ValidateAndNormalize))
        {
            var conflicting = reserved.FirstOrDefault(service => service.Id == candidate.Id);
            if (conflicting is not null && HasSameConfiguration(conflicting, candidate))
            {
                skippedDuplicates++;
                continue;
            }

            var addition = candidate;
            if (conflicting is not null)
            {
                addition = candidate with
                {
                    Id = ServiceIdGenerator.CreateUnique(reserved, candidate.Id)
                };
                reassignedIds++;
            }

            additions.Add(addition);
            reserved.Add(addition);
        }

        return new LinuxServiceImportPlan(additions, skippedDuplicates, reassignedIds);
    }

    private static ServiceDefinition ValidateAndNormalize(ServiceDefinition service)
    {
        if (string.IsNullOrWhiteSpace(service.Id))
        {
            throw new JsonException("A service has no valid id.");
        }

        if (string.IsNullOrWhiteSpace(service.Name))
        {
            throw new JsonException($"Service '{service.Id}' has no valid name.");
        }

        if (!ServiceUrl.TryNormalize(service.Url, out var normalizedUrl))
        {
            throw new JsonException($"Service '{service.Id}' has an invalid HTTP or HTTPS URL.");
        }

        return service with
        {
            Id = service.Id.Trim(),
            Name = service.Name.Trim(),
            Url = normalizedUrl,
            UserAgent = string.IsNullOrWhiteSpace(service.UserAgent)
                ? null
                : service.UserAgent.Trim()
        };
    }

    private static bool HasSameConfiguration(ServiceDefinition left, ServiceDefinition right) =>
        left.Name == right.Name
        && left.Url == right.Url
        && left.Muted == right.Muted
        && left.Disabled == right.Disabled
        && left.UserAgent == right.UserAgent;
}

public sealed record LinuxServiceImportPlan(
    IReadOnlyList<ServiceDefinition> ServicesToAdd,
    int SkippedDuplicates,
    int ReassignedIds);
