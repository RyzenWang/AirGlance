using System.Diagnostics;
using AirGlance.Core.Storage;

namespace AirGlance.App.Services;

/// <summary>
/// Process-wide services: one settings file, one language, one theme and one thumbnail
/// cache. Windows own their own view models.
/// </summary>
public sealed class AppServices : IDisposable
{
    private bool _disposed;

    public AppServices(string? settingsDirectory = null, string? cacheDirectory = null)
    {
        SettingsStore = new SettingsService(settingsDirectory);
        Settings = SettingsStore.Load();
        Thumbnails = new ThumbnailCache(cacheDirectory);

        Localization = new LocalizationService();
        Localization.Apply(Settings.Language);

        Theme = new ThemeService(Settings.Theme);

        // Old thumbnails are removed in the background; startup never waits for it.
        _ = Task.Run(() => Thumbnails.Cleanup(TimeSpan.FromDays(30)));
    }

    public SettingsService SettingsStore { get; }

    public AppSettings Settings { get; }

    public LocalizationService Localization { get; }

    public ThemeService Theme { get; }

    public ThumbnailCache Thumbnails { get; }

    public event EventHandler? SettingsChanged;

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLanguage(string language)
    {
        Settings.Language = language;
        Localization.Apply(language);
        SaveSettings();
    }

    public void SetTheme(string mode)
    {
        Settings.Theme = ThemeService.Normalize(mode);
        Theme.Set(Settings.Theme);
        SaveSettings();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SaveSettings();
        Theme.Dispose();
        Localization.Dispose();
    }

    public static string Version
    {
        get
        {
            var assembly = typeof(AppServices).Assembly;
            var informational = assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                var metadata = informational.IndexOf('+');
                return metadata > 0 ? informational[..metadata] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        }
    }

    public static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best effort; the app itself never uses the network.
        }
    }
}
