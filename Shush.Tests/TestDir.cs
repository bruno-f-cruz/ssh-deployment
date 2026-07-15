namespace Shush.Tests;

/// <summary>Creates throwaway temp directories for filesystem-touching tests.</summary>
public static class TestDir
{
    public static string New()
    {
        var dir = Path.Combine(Path.GetTempPath(), "shush-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string WithFiles(params (string Name, string Content)[] files)
    {
        var dir = New();
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }
}
