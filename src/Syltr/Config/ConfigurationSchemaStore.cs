using System.Text.Json.Serialization;

namespace Syltr.Config;

public sealed record ConfigurationSchemaState
{
    [JsonPropertyName("version")]
    public required int Version { get; init; }
}

/// <summary>
/// Maintains a sidecar schema marker without changing the Linux-compatible JSON files.
/// </summary>
public sealed class ConfigurationSchemaStore(ApplicationDataPaths paths)
{
    public const int CurrentVersion = 1;

    public async Task<ConfigurationSchemaState> EnsureCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SchemaFile))
        {
            var initialState = new ConfigurationSchemaState { Version = CurrentVersion };
            await SaveAsync(initialState, cancellationToken);
            return initialState;
        }

        var state = await AtomicJsonFile.ReadAsync<ConfigurationSchemaState>(
            paths.SchemaFile,
            ConfigurationJson.Options,
            cancellationToken)
            ?? throw new InvalidDataException("The configuration schema marker contains JSON null.");

        if (state.Version != CurrentVersion)
        {
            throw new NotSupportedException(
                $"Configuration schema {state.Version} cannot be opened by schema {CurrentVersion} without a registered migration.");
        }

        return state;
    }

    private Task SaveAsync(
        ConfigurationSchemaState state,
        CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            paths.SchemaFile,
            state,
            ConfigurationJson.Options,
            cancellationToken);
}
