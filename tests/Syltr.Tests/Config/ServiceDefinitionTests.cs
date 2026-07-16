using System.Text.Json;
using Syltr.Config;

namespace Syltr.Tests.Config;

public class ServiceDefinitionTests
{
    [Fact]
    public void OptionalPropertiesHaveReferenceCompatibleDefaults()
    {
        var service = CreateService("whatsapp");

        Assert.False(service.Muted);
        Assert.False(service.Disabled);
        Assert.Null(service.UserAgent);
    }

    [Fact]
    public void PersistedPropertyNamesMatchTheReferenceSchema()
    {
        var json = JsonSerializer.Serialize(CreateService("whatsapp"));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("id").GetString());
        Assert.Equal("WhatsApp", root.GetProperty("name").GetString());
        Assert.Equal("https://web.whatsapp.com", root.GetProperty("url").GetString());
        Assert.False(root.GetProperty("muted").GetBoolean());
        Assert.False(root.GetProperty("disabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("user_agent").ValueKind);
    }

    [Fact]
    public void SettingsStartWithNoSpellcheckLanguages()
    {
        var settings = new ApplicationSettings();

        Assert.Empty(settings.SpellLanguages);
    }

    private static ServiceDefinition CreateService(string id) => new()
    {
        Id = id,
        Name = "WhatsApp",
        Url = "https://web.whatsapp.com"
    };
}
