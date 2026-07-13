namespace Shush.Design.Services;

public static class ShushPaths
{
    public static string GetRepoRoot(IWebHostEnvironment env) =>
        Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));

    public static string GetShushDirectory(IWebHostEnvironment env)
    {
        var dir = Path.Combine(GetRepoRoot(env), ".shush");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
