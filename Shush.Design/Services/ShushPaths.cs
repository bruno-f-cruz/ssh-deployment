using Shush;

namespace Shush.Design.Services;

public sealed record AppLogPath(string Path);

/// <summary>Where the app keeps its data: the database, run logs and data-protection keys, all under one folder.</summary>
public sealed class ShushPaths
{
    public ShushPaths(string contentRoot, ShushSettings settings)
    {
        DataDirectory = Path.Combine(contentRoot, settings.DataDirectoryName);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(KeysDirectory);
    }

    public string DataDirectory { get; }
    public string DatabaseFile => Path.Combine(DataDirectory, "shush.db");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string KeysDirectory => Path.Combine(DataDirectory, "keys");

    /// <summary>Shipped content, next to the binary.</summary>
    public static string Shipped(string folder) => Path.Combine(AppContext.BaseDirectory, folder);
}
