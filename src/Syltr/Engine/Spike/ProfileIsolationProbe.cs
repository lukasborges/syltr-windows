using System.Collections.ObjectModel;
using System.Text.Json;

namespace Syltr.Engine.Spike;

public sealed record ProfileStorageProbe(
    string ProfileName,
    string? PreviousValue,
    string WrittenValue,
    string? ReadBackValue);

public sealed record ProfileIsolationProbeResult(
    bool CurrentRunIsIsolated,
    bool PersistenceWasDetected,
    IReadOnlyList<ProfileStorageProbe> Profiles)
{
    public static ProfileIsolationProbeResult Evaluate(IEnumerable<ProfileStorageProbe> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = profiles.ToArray();
        var currentValues = snapshot.Select(profile => profile.ReadBackValue).ToArray();
        var previousValues = snapshot.Select(profile => profile.PreviousValue).ToArray();

        var currentRunIsIsolated = snapshot.Length >= 2
            && snapshot.All(profile => profile.ReadBackValue == profile.WrittenValue)
            && currentValues.All(value => value is not null)
            && currentValues.Distinct(StringComparer.Ordinal).Count() == snapshot.Length;
        var persistenceWasDetected = snapshot.Length >= 2
            && snapshot.All(profile =>
                profile.PreviousValue?.StartsWith($"{profile.ProfileName}:", StringComparison.Ordinal) == true)
            && previousValues.Distinct(StringComparer.Ordinal).Count() == snapshot.Length;

        return new ProfileIsolationProbeResult(
            currentRunIsIsolated,
            persistenceWasDetected,
            new ReadOnlyCollection<ProfileStorageProbe>(snapshot));
    }
}

/// <summary>
/// Verifies that identical origins store distinct persistent values in different profiles.
/// </summary>
public static class ProfileIsolationProbe
{
    private const string StorageKey = "syltr-profile-isolation-probe";

    public static async Task<ProfileIsolationProbeResult> RunAsync(
        IEnumerable<ServiceViewHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        var snapshot = hosts.ToArray();
        if (snapshot.Length < 2)
        {
            throw new ArgumentException("At least two profiles are required for an isolation probe.", nameof(hosts));
        }

        var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var host in snapshot)
        {
            previous[host.ProfileName] = await ReadStorageAsync(host);
            var value = $"{host.ProfileName}:{Guid.NewGuid():N}";
            written[host.ProfileName] = value;
            await WriteStorageAsync(host, value);
        }

        var probes = new List<ProfileStorageProbe>(snapshot.Length);
        foreach (var host in snapshot)
        {
            probes.Add(new ProfileStorageProbe(
                host.ProfileName,
                previous[host.ProfileName],
                written[host.ProfileName],
                await ReadStorageAsync(host)));
        }

        return ProfileIsolationProbeResult.Evaluate(probes);
    }

    private static async Task<string?> ReadStorageAsync(ServiceViewHost host)
    {
        var key = JsonSerializer.Serialize(StorageKey);
        var result = await host.ExecuteScriptAsync($"localStorage.getItem({key})");
        return JsonSerializer.Deserialize<string?>(result);
    }

    private static async Task WriteStorageAsync(ServiceViewHost host, string value)
    {
        var keyJson = JsonSerializer.Serialize(StorageKey);
        var valueJson = JsonSerializer.Serialize(value);
        await host.ExecuteScriptAsync($"localStorage.setItem({keyJson}, {valueJson}); true;");
    }
}
