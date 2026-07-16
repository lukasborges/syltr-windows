using System.Text.Json;
using Syltr.Config;

namespace Syltr.Tests.Config;

public class ConfigurationStoreTests
{
    [Fact]
    public async Task FirstLoadCreatesEmptyReferenceCompatibleFiles()
    {
        using var data = new TemporaryApplicationData();
        var servicesStore = new ServiceConfigurationStore(data.Paths);
        var settingsStore = new SettingsConfigurationStore(data.Paths);

        var services = await servicesStore.LoadAsync();
        var settings = await settingsStore.LoadAsync();

        Assert.Equal(ConfigurationLoadStatus.Created, services.Status);
        Assert.Empty(services.Value);
        Assert.Equal("[]", (await File.ReadAllTextAsync(data.Paths.ServicesFile)).Trim());
        Assert.Equal(ConfigurationLoadStatus.Created, settings.Status);
        Assert.Empty(settings.Value.SpellLanguages);
        Assert.True(File.Exists(data.Paths.SettingsFile));
    }

    [Fact]
    public async Task ServicesRoundTripWithLinuxCompatibleSchema()
    {
        using var data = new TemporaryApplicationData();
        var store = new ServiceConfigurationStore(data.Paths);
        var expected = new ServiceDefinition
        {
            Id = "slack-work",
            Name = "Slack Work",
            Url = "https://app.slack.com/client",
            Muted = true,
            UserAgent = "Syltr Test"
        };

        await store.SaveAsync([expected]);
        var loaded = await store.LoadAsync();

        Assert.Equal(ConfigurationLoadStatus.Loaded, loaded.Status);
        Assert.Equal(expected, Assert.Single(loaded.Value));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(data.Paths.ServicesFile));
        var service = json.RootElement[0];
        Assert.Equal("slack-work", service.GetProperty("id").GetString());
        Assert.Equal("Syltr Test", service.GetProperty("user_agent").GetString());
    }

    [Fact]
    public async Task MissingOptionalServicePropertiesUseDefaults()
    {
        using var data = new TemporaryApplicationData();
        Directory.CreateDirectory(data.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(
            data.Paths.ServicesFile,
            """[{"id":"gmail","name":"Gmail","url":"https://mail.google.com/"}]""");

        var loaded = await new ServiceConfigurationStore(data.Paths).LoadAsync();

        var service = Assert.Single(loaded.Value);
        Assert.False(service.Muted);
        Assert.False(service.Disabled);
        Assert.Null(service.UserAgent);
    }

    [Fact]
    public async Task CorruptedFilesReturnSafeDefaultsWithoutOverwritingInput()
    {
        using var data = new TemporaryApplicationData();
        Directory.CreateDirectory(data.Paths.ConfigDirectory);
        const string corrupted = "{ definitely not json";
        await File.WriteAllTextAsync(data.Paths.ServicesFile, corrupted);
        await File.WriteAllTextAsync(data.Paths.SettingsFile, corrupted);

        var services = await new ServiceConfigurationStore(data.Paths).LoadAsync();
        var settings = await new SettingsConfigurationStore(data.Paths).LoadAsync();

        Assert.Equal(ConfigurationLoadStatus.Corrupted, services.Status);
        Assert.Empty(services.Value);
        Assert.IsType<JsonException>(services.Error);
        Assert.Equal(ConfigurationLoadStatus.Corrupted, settings.Status);
        Assert.Empty(settings.Value.SpellLanguages);
        Assert.Equal(corrupted, await File.ReadAllTextAsync(data.Paths.ServicesFile));
        Assert.Equal(corrupted, await File.ReadAllTextAsync(data.Paths.SettingsFile));
    }

    [Fact]
    public async Task AtomicSaveLeavesNoTemporaryFiles()
    {
        using var data = new TemporaryApplicationData();
        var store = new ServiceConfigurationStore(data.Paths);

        await store.SaveAsync([CreateService("slack")]);
        await store.SaveAsync([CreateService("teams")]);

        var loaded = await store.LoadAsync();
        Assert.Equal("teams", Assert.Single(loaded.Value).Id);
        Assert.Empty(Directory.EnumerateFiles(data.Paths.ConfigDirectory, "*.tmp"));
    }

    [Fact]
    public async Task SettingsRoundTrip()
    {
        using var data = new TemporaryApplicationData();
        var store = new SettingsConfigurationStore(data.Paths);
        var expected = new ApplicationSettings
        {
            DoNotDisturb = true,
            SpellLanguages = ["en_US", "pt_BR"]
        };

        await store.SaveAsync(expected);
        var loaded = await store.LoadAsync();

        Assert.Equal(ConfigurationLoadStatus.Loaded, loaded.Status);
        Assert.Equal(expected.SpellLanguages, loaded.Value.SpellLanguages);
    }

    private static ServiceDefinition CreateService(string id) => new()
    {
        Id = id,
        Name = id,
        Url = "https://example.com"
    };
}
