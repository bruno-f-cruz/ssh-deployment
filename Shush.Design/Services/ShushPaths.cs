using Shush;

namespace Shush.Design.Services;

public sealed record AppLogPath(string Path);

public static class ShushPaths
{
    public static string GetShushDirectory(IWebHostEnvironment env, ShushSettings settings)
    {
        var dir = Path.Combine(env.ContentRootPath, settings.DataDirectoryName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Directory for user-uploaded / user-edited recipe YAML files (overlays the built-ins by name).</summary>
    public static string GetUserRecipesDirectory(IWebHostEnvironment env, ShushSettings settings)
    {
        var dir = Path.Combine(GetShushDirectory(env, settings), "recipes");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
