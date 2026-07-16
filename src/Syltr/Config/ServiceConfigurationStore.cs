using System.Text.Json;

namespace Syltr.Config;

/// <summary>
/// Loads and atomically saves the reference-compatible services.json array.
/// </summary>
public sealed class ServiceConfigurationStore(ApplicationDataPaths paths)
{
    public async Task<ConfigurationLoadResult<IReadOnlyList<ServiceDefinition>>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(paths.ServicesFile))
            {
                IReadOnlyList<ServiceDefinition> empty = [];
                await SaveAsync(empty, cancellationToken);
                return new ConfigurationLoadResult<IReadOnlyList<ServiceDefinition>>(
                    empty,
                    ConfigurationLoadStatus.Created);
            }

            var services = await AtomicJsonFile.ReadAsync<List<ServiceDefinition>>(
                paths.ServicesFile,
                ConfigurationJson.Options,
                cancellationToken);
            return services is null
                ? Corrupted(new JsonException("The services file contains JSON null."))
                : new ConfigurationLoadResult<IReadOnlyList<ServiceDefinition>>(
                    services.AsReadOnly(),
                    ConfigurationLoadStatus.Loaded);
        }
        catch (JsonException exception)
        {
            return Corrupted(exception);
        }
        catch (IOException exception)
        {
            return Unreadable(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unreadable(exception);
        }
    }

    public Task SaveAsync(
        IEnumerable<ServiceDefinition> services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AtomicJsonFile.WriteAsync(
            paths.ServicesFile,
            services.ToArray(),
            ConfigurationJson.Options,
            cancellationToken);
    }

    private static ConfigurationLoadResult<IReadOnlyList<ServiceDefinition>> Corrupted(Exception error) =>
        new([], ConfigurationLoadStatus.Corrupted, error);

    private static ConfigurationLoadResult<IReadOnlyList<ServiceDefinition>> Unreadable(Exception error) =>
        new([], ConfigurationLoadStatus.Unreadable, error);
}
