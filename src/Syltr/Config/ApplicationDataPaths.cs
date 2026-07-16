namespace Syltr.Config;

/// <summary>
/// Centralizes every mutable path owned by Syltr.
/// </summary>
public sealed class ApplicationDataPaths
{
    public ApplicationDataPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string ConfigDirectory => Path.Combine(RootDirectory, "config");

    public string WebViewDirectory => Path.Combine(RootDirectory, "webview");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string MigrationsDirectory => Path.Combine(RootDirectory, "migrations");

    public string ServicesFile => Path.Combine(ConfigDirectory, "services.json");

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public string SchemaFile => Path.Combine(MigrationsDirectory, "schema.json");

    public static ApplicationDataPaths ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("Windows local application data folder is unavailable.");
        }

        return new ApplicationDataPaths(Path.Combine(localApplicationData, "Syltr"));
    }
}
