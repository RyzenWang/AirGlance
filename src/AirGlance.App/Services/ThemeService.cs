using AirGlance.App.Styles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace AirGlance.App.Services;

/// <summary>
/// Owns the application theme. Light and dark are explicit choices; "system" follows
/// the OS, including its high-contrast preference. Switching never restarts a window.
/// </summary>
public sealed class ThemeService : IDisposable
{
    public const string SystemMode = "System";
    public const string LightMode = "Light";
    public const string DarkMode = "Dark";

    private IResourceProvider? _highContrastPalette;

    public ThemeService(string? initialMode = null)
    {
        Mode = Normalize(initialMode);
        IsDark = ResolveIsDark(Mode);
        ApplyToApplication();
        SubscribeToPlatform();

        if (Application.Current?.PlatformSettings is { } platform &&
            GetColorValues(platform) is { } values)
        {
            IsHighContrast = values.ContrastPreference == ColorContrastPreference.High;
        }
    }

    public string Mode { get; private set; }

    public bool IsDark { get; private set; }

    public bool IsHighContrast { get; private set; }

    public bool IsSystem => string.Equals(Mode, SystemMode, StringComparison.Ordinal);

    public event EventHandler? Changed;

    public static string Normalize(string? mode) => mode switch
    {
        LightMode => LightMode,
        DarkMode => DarkMode,
        _ => SystemMode
    };

    public void Set(string mode)
    {
        Mode = Normalize(mode);
        IsDark = ResolveIsDark(Mode);
        ApplyToApplication();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Application.Current?.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged -= OnPlatformColorValuesChanged;
        }

        RemoveHighContrastPalette();
    }

    private static bool ResolveIsDark(string mode) => mode switch
    {
        DarkMode => true,
        LightMode => false,
        _ => SystemIsDark()
    };

    private static bool SystemIsDark()
    {
        try
        {
            return Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        }
        catch
        {
            return false;
        }
    }

    private static PlatformColorValues? GetColorValues(IPlatformSettings platformSettings)
    {
        try
        {
            return platformSettings.GetColorValues();
        }
        catch
        {
            return null;
        }
    }

    private void SubscribeToPlatform()
    {
        if (Application.Current?.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        }
    }

    private void ApplyToApplication()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = Mode switch
        {
            LightMode => ThemeVariant.Light,
            DarkMode => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        ApplyHighContrastPalette(application);
    }

    private void ApplyHighContrastPalette(Application application)
    {
        var highContrast = SystemHighContrast();

        if (!highContrast)
        {
            RemoveHighContrastPalette();
            IsHighContrast = false;
            return;
        }

        if (_highContrastPalette is not null)
        {
            IsHighContrast = true;
            return;
        }

        try
        {
            var palette = new HighContrastPalette();
            ApplySystemAccent(palette);
            application.Resources.MergedDictionaries.Add(palette);
            _highContrastPalette = palette;
            IsHighContrast = true;
        }
        catch
        {
            // A missing palette must not stop the window from opening.
        }
    }

    private static void ApplySystemAccent(HighContrastPalette palette)
    {
        try
        {
            if (Application.Current?.PlatformSettings?.GetColorValues() is not { } values)
            {
                return;
            }

            // The only per-system colour Avalonia exposes is the accent; use it for
            // focus and current-item marks so contrast themes still look native.
            palette["Coral"] = new SolidColorBrush(values.AccentColor1);
            palette["Teal"] = new SolidColorBrush(values.AccentColor1);
        }
        catch
        {
            // Keep the fallback palette.
        }
    }

    private void RemoveHighContrastPalette()
    {
        if (_highContrastPalette is not null && Application.Current is { } application)
        {
            application.Resources.MergedDictionaries.Remove(_highContrastPalette);
        }

        _highContrastPalette = null;
        IsHighContrast = false;
    }

    private static bool SystemHighContrast()
    {
        try
        {
            return Application.Current?.PlatformSettings?.GetColorValues().ContrastPreference ==
                   ColorContrastPreference.High;
        }
        catch
        {
            return false;
        }
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsSystem)
            {
                IsDark = e.ThemeVariant == PlatformThemeVariant.Dark;
                ApplyToApplication();
            }
            else
            {
                // High contrast follows the system even when light/dark is explicit.
                ApplyHighContrastPalette(Application.Current!);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
