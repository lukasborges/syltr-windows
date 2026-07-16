using System.Text.Json;
using Syltr.Config;

namespace Syltr.Tests.Config;

public sealed class LinuxServiceImporterTests
{
    [Fact]
    public async Task Reads_reference_linux_schema_and_normalizes_values()
    {
        using var data = new TemporaryApplicationData();
        var path = Path.Combine(data.Paths.RootDirectory, "linux-services.json");
        Directory.CreateDirectory(data.Paths.RootDirectory);
        await File.WriteAllTextAsync(
            path,
            """
            [
              {
                "id": " slack-work ",
                "name": " Slack Work ",
                "url": "app.slack.com/client",
                "muted": true,
                "disabled": false,
                "user_agent": " Custom UA "
              }
            ]
            """);

        var service = Assert.Single(await LinuxServiceImporter.ReadAsync(path));

        Assert.Equal("slack-work", service.Id);
        Assert.Equal("Slack Work", service.Name);
        Assert.Equal("https://app.slack.com/client", service.Url);
        Assert.True(service.Muted);
        Assert.False(service.Disabled);
        Assert.Equal("Custom UA", service.UserAgent);
    }

    [Fact]
    public async Task Rejects_invalid_services_without_modifying_the_source()
    {
        using var data = new TemporaryApplicationData();
        var path = Path.Combine(data.Paths.RootDirectory, "services.json");
        const string json = """[{"id":"mail","name":"Mail","url":"file:///tmp/mail"}]""";
        Directory.CreateDirectory(data.Paths.RootDirectory);
        await File.WriteAllTextAsync(path, json);

        await Assert.ThrowsAsync<JsonException>(() => LinuxServiceImporter.ReadAsync(path));

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void Merge_skips_exact_duplicates_and_reassigns_conflicting_ids()
    {
        var existing = Create("slack", "Slack", "https://app.slack.com/client");
        var exactDuplicate = existing with { };
        var idCollision = Create("slack", "Slack Personal", "https://app.slack.com/client");
        var unique = Create("gmail", "Gmail", "https://mail.google.com/");

        var plan = LinuxServiceImporter.CreatePlan(
            [existing],
            [exactDuplicate, idCollision, unique]);

        Assert.Equal(2, plan.ServicesToAdd.Count);
        Assert.Equal(1, plan.SkippedDuplicates);
        Assert.Equal(1, plan.ReassignedIds);
        Assert.Equal("slack-2", plan.ServicesToAdd[0].Id);
        Assert.Equal("gmail", plan.ServicesToAdd[1].Id);
        Assert.Equal("slack", existing.Id);
    }

    private static ServiceDefinition Create(string id, string name, string url) => new()
    {
        Id = id,
        Name = name,
        Url = url
    };
}
