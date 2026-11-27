using AirGlance.App.Services;
using AirGlance.Core.Files;
using AirGlance.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirGlance.App.ViewModels;

/// <summary>
/// Settings apply as soon as they are changed; there is no OK button to forget. Every
/// value is normalised by <see cref="AppSettings"/> before it is saved.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly int[] SlideIntervals = [3, 5, 10];
    private readonly AppServices _services;
    private bool _initializing = true;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        var settings = services.Settings;

        _themeIndex = settings.Theme switch
        {
            ThemeService.LightMode => 1,
            ThemeService.DarkMode => 2,
            _ => 0
        };
        _languageIndex = settings.Language == LocalizationService.ChineseSimplified ? 1 : 0;
        var intervalIndex = Array.IndexOf(SlideIntervals, settings.SlideshowSeconds);
        _slideshowIndex = intervalIndex >= 0 ? intervalIndex : 1;
        var sortOrder = settings.SortOrder;
        _sortIndex = (int)sortOrder.Field * 2 + (sortOrder.Descending ? 1 : 0);
        _checkerboard = settings.Checkerboard;
        _rememberZoom = settings.RememberZoom;
        _lockZoom = settings.LockZoom;
        _cacheSizeText = FormatCacheSize();

        _initializing = false;
    }

    public string VersionText => _services.Localization.Format("L.AboutVersion", AppServices.Version);

    public string GitHubUrl => "https://github.com/RyzenWang/AirGlance";

    public string CacheDescription => _services.Localization.T("L.CacheDescription");

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private int _slideshowIndex;

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private bool _checkerboard;

    [ObservableProperty]
    private bool _rememberZoom;

    [ObservableProperty]
    private bool _lockZoom;

    [ObservableProperty]
    private string _cacheSizeText = string.Empty;

    [ObservableProperty]
    private string? _cacheMessage;

    partial void OnThemeIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }

        _services.SetTheme(value switch
        {
            1 => ThemeService.LightMode,
            2 => ThemeService.DarkMode,
            _ => ThemeService.SystemMode
        });
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }

        _services.SetLanguage(value == 1 ? LocalizationService.ChineseSimplified : LocalizationService.English);
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(CacheDescription));
    }

    partial void OnSlideshowIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }

        _services.Settings.SlideshowSeconds = SlideIntervals[Math.Clamp(value, 0, SlideIntervals.Length - 1)];
        _services.SaveSettings();
    }

    partial void OnSortIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }

        // The combo lists each field twice, ascending then descending, so the index maps
        // straight onto the order without a lookup table.
        var order = new FolderSortOrder((FolderSortField)(value / 2), value % 2 == 1);
        _services.Settings.Sort = order.ToSettingValue();
        _services.SaveSettings();
    }

    partial void OnCheckerboardChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        _services.Settings.Checkerboard = value;
        _services.SaveSettings();
    }

    partial void OnLockZoomChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        _services.Settings.LockZoom = value;
        _services.SaveSettings();
    }

    partial void OnRememberZoomChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        _services.Settings.RememberZoom = value;
        _services.SaveSettings();
    }

    [RelayCommand]
    private void ClearCache()
    {
        _services.Thumbnails.Clear();
        CacheSizeText = FormatCacheSize();
        CacheMessage = _services.Localization.T("L.CacheCleared");
        OnPropertyChanged(nameof(CacheSizeText));
    }

    private string FormatCacheSize()
    {
        var megabytes = _services.Thumbnails.GetSizeBytes() / (1024.0 * 1024.0);
        return _services.Localization.Format(
            "L.CacheSizeFormat",
            megabytes.ToString("0.0", _services.Localization.Culture));
    }
}
