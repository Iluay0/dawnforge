namespace Dawnforge.Core;

public static class AppPaths
{
    public static string ConfigDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dawnforge")
        : Path.Combine(XdgDirectory("XDG_CONFIG_HOME", ".config"), "dawnforge");

    public static string CacheDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dawnforge")
        : Path.Combine(XdgDirectory("XDG_CACHE_HOME", ".cache"), "dawnforge");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "dawnforge.json");

    private static string XdgDirectory(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value))
        {
            return value;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
    }
}
