using System.Text.Json;

namespace Syltr.Config;

/// <summary>
/// Loads and atomically saves application-wide settings.
/// </summary>
public sealed class SettingsConfigurationStore(ApplicationDataPaths paths)
{
    public async Task<ConfigurationLoadResult<ApplicationSettings>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(paths.SettingsFile))
            {
                var defaults = new ApplicationSettings();
                await SaveAsync(defaults, cancellationToken);
                return new ConfigurationLoadResult<ApplicationSettings>(
                    defaults,
                    ConfigurationLoadStatus.Created);
            }

            var settings = await AtomicJsonFile.ReadAsync<ApplicationSettings>(
                paths.SettingsFile,
                ConfigurationJson.Options,
                cancellationToken);
            return settings is null
                ? Corrupted(new JsonException("The settings file contains JSON null."))
                : new ConfigurationLoadResult<ApplicationSettings>(
                    settings,
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
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return AtomicJsonFile.WriteAsync(
            paths.SettingsFile,
            settings,
            ConfigurationJson.Options,
            cancellationToken);
    }

    private static ConfigurationLoadResult<ApplicationSettings> Corrupted(Exception error) =>
        new(new ApplicationSettings(), ConfigurationLoadStatus.Corrupted, error);

    private static ConfigurationLoadResult<ApplicationSettings> Unreadable(Exception error) =>
        new(new ApplicationSettings(), ConfigurationLoadStatus.Unreadable, error);
}
