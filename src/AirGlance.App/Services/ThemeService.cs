using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace AirGlance.App.Services;

/// <summary>
/// Owns the application theme. Light and dark are explicit choices; "system" follows
/// the OS. Switching never restarts a window.
/// </summary>
public sealed class ThemeService : IDisposable
{
    public const string SystemMode = "System";
    public const string LightMode = "Light";
    public const string DarkMode = "Dark";

    public ThemeService(string? initialMode = null)
    {
        Mode = Normalize(initialMode);
        IsDark = ResolveIsDark(Mode);
        ApplyToApplication();
        SubscribeToPlatform();
    }

    public string Mode { get; private set; }

    public bool IsDark { get; private set; }

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
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // A light/dark choice is explicit; only "system" follows the OS.
            if (!IsSystem)
            {
                return;
            }

            IsDark = e.ThemeVariant == PlatformThemeVariant.Dark;
            ApplyToApplication();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
