using System.Globalization;

namespace AirGlance.Core.Storage;

/// <summary>
/// Platform folders for settings, cache and temporary data. Kept in one place so the
/// packaged and portable builds agree on where a file lives.
/// </summary>
public static class AppPaths
{
    public static string SettingsDirectory { get; } = ResolveSettingsDirectory();

    public static string CacheDirectory { get; } = ResolveCacheDirectory();

    public static string ThumbnailDirectory => Path.Combine(CacheDirectory, "thumbs");

    private static string ResolveSettingsDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("AIRGLANCE_SETTINGS_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return MarkAsUserOverride(overridePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(HomeDirectory(), "Library", "Application Support", "AirGlance");
        }

        if (OperatingSystem.IsLinux())
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(config))
            {
                config = Path.Combine(HomeDirectory(), ".config");
            }

            return Path.Combine(MarkAsUserOverride(config), "airglance");
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roaming))
        {
            roaming = Path.Combine(HomeDirectory(), "AppData", "Roaming");
        }

        return Path.Combine(roaming, "AirGlance");
    }

    private static string ResolveCacheDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("AIRGLANCE_CACHE_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return MarkAsUserOverride(overridePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(HomeDirectory(), "Library", "Caches", "AirGlance");
        }

        if (OperatingSystem.IsLinux())
        {
            var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cache))
            {
                cache = Path.Combine(HomeDirectory(), ".cache");
            }

            return Path.Combine(MarkAsUserOverride(cache), "airglance");
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
        {
            local = Path.Combine(HomeDirectory(), "AppData", "Local");
        }

        return Path.Combine(local, "AirGlance", "cache");
    }

    private static string HomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
            ? home
            : Path.GetTempPath();

    private static string MarkAsUserOverride(string path) => Path.GetFullPath(path);

    public static string DefaultLanguage =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en";
}
