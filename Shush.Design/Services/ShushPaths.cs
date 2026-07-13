using Shush;

namespace Shush.Design.Services;

public static class ShushPaths
{
    public static string GetShushDirectory(IWebHostEnvironment env, ShushSettings settings)
    {
        var dir = Path.Combine(env.ContentRootPath, settings.DataDirectoryName);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
