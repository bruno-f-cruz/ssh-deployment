using Shush;

namespace Shush.Design.Services;

public sealed record AppLogPath(string Path);

/// <summary>Directories recipes are discovered from: shipped built-ins (base) and user copies (user).</summary>
public sealed record RecipePaths(string BaseDir, string UserDir);

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
