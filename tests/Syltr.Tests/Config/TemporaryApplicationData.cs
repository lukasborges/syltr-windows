namespace Syltr.Tests.Config;

internal sealed class TemporaryApplicationData : IDisposable
{
    public TemporaryApplicationData()
    {
        Root = Path.Combine(Path.GetTempPath(), "Syltr.Tests", Guid.NewGuid().ToString("N"));
        Paths = new Syltr.Config.ApplicationDataPaths(Root);
    }

    public string Root { get; }

    public Syltr.Config.ApplicationDataPaths Paths { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
