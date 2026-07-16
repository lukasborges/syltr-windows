using Syltr.Config;

namespace Syltr.Tests.Config;

public class ConfigurationSchemaStoreTests
{
    [Fact]
    public async Task CreatesSeparateCurrentVersionMarker()
    {
        using var data = new TemporaryApplicationData();
        var store = new ConfigurationSchemaStore(data.Paths);

        var state = await store.EnsureCurrentAsync();

        Assert.Equal(ConfigurationSchemaStore.CurrentVersion, state.Version);
        Assert.True(File.Exists(data.Paths.SchemaFile));
        Assert.False(File.Exists(data.Paths.ServicesFile));
    }

    [Fact]
    public async Task RejectsVersionWithoutRegisteredMigration()
    {
        using var data = new TemporaryApplicationData();
        Directory.CreateDirectory(data.Paths.MigrationsDirectory);
        await File.WriteAllTextAsync(data.Paths.SchemaFile, """{"version":2}""");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => new ConfigurationSchemaStore(data.Paths).EnsureCurrentAsync());

        Assert.Contains("schema 2", exception.Message);
    }
}
