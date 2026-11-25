using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using AirGlance.App.Controls;
using AirGlance.App.Imaging;
using AirGlance.App.Rendering;
using AirGlance.App.Services;
using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
using AirGlance.Core.Navigation;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirGlance.App.ViewModels;

public sealed class ImagePresentedEventArgs : EventArgs
{
    public ImagePresentedEventArgs(
        WriteableBitmap? bitmap,
        int sourceWidth,
        int sourceHeight,
        double imageScale,
        bool resetView,
        bool animate,
        bool isNavigation,
        AnimatedImage? animation = null)
    {
        Bitmap = bitmap;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        ImageScale = imageScale;
        ResetView = resetView;
        Animate = animate;
        IsNavigation = isNavigation;
        Animation = animation;
    }

    public WriteableBitmap? Bitmap { get; }

    public AnimatedImage? Animation { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public double ImageScale { get; }

    public bool ResetView { get; }

    public bool Animate { get; }

    public bool IsNavigation { get; }
}

public sealed class TiledImagePresentedEventArgs(TiledImageSource tiles) : EventArgs
{
    public TiledImageSource Tiles { get; } = tiles;
}

/// <summary>
/// The viewer's state: one folder, one current image, one display-only edit, and the
/// information panel. Decoding and thumbnail work happen off this class's thread; only
/// the final bitmap copy and property updates run on the UI thread.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const long TiledPixelThreshold = 40_000_000;
    private const int ProxyMaximumEdge = 8192;
    private const double RegionActivationPercent = 30;

    private readonly AppServices _services;
    private readonly ImageLoadService _loader = new();
    private readonly ImageNavigator _navigator = new([]);
    private readonly Dictionary<string, ImageMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task<PreloadedImage?>? _startupPreload;
    private readonly DispatcherTimer _messageTimer;
    private readonly DispatcherTimer _slideshowTimer;
    private readonly DispatcherTimer _resizeTimer;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _prefetchCts;
    private CancellationTokenSource? _fullCts;
    private CancellationTokenSource? _proxyCts;
    private bool _isUpgradingProxy;
    private bool _isRegionOpening;
    private bool _regionActive;
    private bool _regionLoading;
    private double _regionCacheProgress;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _showRegionLoading;

    [ObservableProperty]
    private string _regionLoadingText = string.Empty;
    private ThumbnailService? _thumbnails;
    private bool _thumbnailsPending;
    private bool _slideshowBusy;
    private int _loadVersion;
    private int _editVersion;
    private bool _isLoadingFull;
    private bool _fullLoaded;
    private DecodedBitmap? _rawDecoded;
    private DecodedBitmap? _displayDecoded;
    private WriteableBitmap? _currentBitmap;
    private AnimatedImage? _currentAnimation;
    private TiledImageSource? _tiledSource;
    private ImageMetadata? _metadata;
    private EditTransform _edit = EditTransform.None;
    private EditTransform _editAtSessionStart = EditTransform.None;
    private CropRegion _crop = CropRegion.Full;
    private string? _currentPath;
    private int _viewportWidth = 1600;
    private int _viewportHeight = 1000;
    private string _appliedSort;
    private ImageLoadFailure? _lastFailure;
    private string? _lastFailureExtension;
    private bool _folderEmpty;
    private bool _disposed;

    public MainWindowViewModel(AppServices services, Task<PreloadedImage?>? startupPreload = null)
    {
        _services = services;
        _startupPreload = startupPreload;
        _appliedSort = services.Settings.Sort;
        CheckerboardEnabled = services.Settings.Checkerboard;
        SlideshowSeconds = services.Settings.SlideshowSeconds;
        Analysis = new AnalysisViewModel(services.Localization);

        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            TransientMessage = null;
        };

        _slideshowTimer = new DispatcherTimer();
        _slideshowTimer.Tick += OnSlideshowTick;

        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _resizeTimer.Tick += OnResizeTick;

        services.Localization.LanguageChanged += OnLanguageChanged;
        services.SettingsChanged += OnSettingsChanged;

        ShowEmptyState();
    }

    public event EventHandler<ImagePresentedEventArgs>? ImagePresented;

    public event EventHandler<TiledImagePresentedEventArgs>? TiledImagePresented;

    public event EventHandler? TiledImageCleared;

    public event EventHandler? CurrentItemChanged;

    public ObservableCollection<ImageItemViewModel> Images { get; } = [];

    public AnalysisViewModel Analysis { get; }

    public string? CurrentFilePath => _currentPath;

    public WriteableBitmap? CurrentBitmap => _currentAnimation?.Frames[0] ?? _currentBitmap;

    public bool IsTiled => _tiledSource is not null;

    /// <summary>
    /// Edit v1 requires a fully decodable, non-tiled still image. Proxy-only large
    /// TIFFs are intentionally read-only so an export can never silently write the
    /// low-resolution preview.
    /// </summary>
    public bool CanEdit =>
        HasImage &&
        _currentPath is not null &&
        _rawDecoded is { IsProxy: false } &&
        _tiledSource is null &&
        _currentAnimation is null;

    public bool IsProxyOnly => _rawDecoded?.IsProxy == true;

    public CropRegion Crop => _crop;

    public async Task<WriteableBitmap?> GetClipboardBitmapAsync(CancellationToken cancellationToken)
    {
        if (_tiledSource is null)
        {
            return CurrentBitmap;
        }

        var path = _currentPath;
        if (path is null)
        {
            return null;
        }

        var full = await _loader.DecodeFullAsync(path, cancellationToken);
        var edit = _edit;
        var display = edit.IsIdentity
            ? full
            : await Task.Run(() => ImageTransform.Apply(full, edit), cancellationToken);
        return BitmapFactory.FromDecoded(display);
    }

    public ImageMetadata? Metadata => _metadata;

    public EditTransform CurrentEdit => _edit;

    public double LastDecodeMilliseconds { get; private set; }

    public bool RememberZoom => _services.Settings.RememberZoom;

    public string InitialZoomMode => _services.Settings.RememberZoom ? _services.Settings.ZoomMode : "fit";

    public double InitialZoomFactor => _services.Settings.ZoomFactor;

    public string Language => _services.Localization.Language;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private ImageItemViewModel? _currentItem;

    [ObservableProperty]
    private bool _hasImage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _infoVisible;

    [ObservableProperty]
    private bool _filmstripVisible;

    [ObservableProperty]
    private bool _analysisVisible;

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isSlideshowRunning;

    [ObservableProperty]
    private string _titleFileName = "AirGlance";

    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _windowTitle = "AirGlance";

    [ObservableProperty]
    private string _statusDimensions = string.Empty;

    [ObservableProperty]
    private string _statusPosition = string.Empty;

    [ObservableProperty]
    private string _zoomText = "100%";

    [ObservableProperty]
    private double _zoomPercent = 100;

    [ObservableProperty]
    private string? _transientMessage;

    [ObservableProperty]
    private bool _overlayVisible;

    [ObservableProperty]
    private string _overlayTitle = string.Empty;

    [ObservableProperty]
    private string _overlayMessage = string.Empty;

    [ObservableProperty]
    private string _overlayHint = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private IReadOnlyList<InfoSection> _infoSections = [];

    [ObservableProperty]
    private bool _checkerboardEnabled = true;

    [ObservableProperty]
    private int _slideshowSeconds = 5;

    public bool IsChromeVisible => !IsFullscreen;

    public bool ShowTitleBar => OperatingSystem.IsWindows() && !IsFullscreen;

    public bool FilmstripShown => FilmstripVisible && !IsFullscreen;

    public string FilmstripTitle => _services.Localization.T("L.Filmstrip");

    public string CollapseFilmstripText => _services.Localization.T("L.CollapseFilmstrip");

    public string FilmstripSummary => Images.Count == 0
        ? string.Empty
        : _services.Localization.Format("L.FilmstripSummary", Images.Count);

    public bool InfoShown => InfoVisible && !IsFullscreen;

    public bool AnalysisShown => AnalysisVisible && !IsFullscreen;

    public string CacheSizeText =>
        _services.Localization.Format(
            "L.CacheSizeFormat",
            (_services.Thumbnails.GetSizeBytes() / (1024.0 * 1024.0)).ToString("0.0", _services.Localization.Culture));

    public async Task InitializeAsync(string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            ShowEmptyState();
            return;
        }

        await OpenPathAsync(initialPath);
    }

    public async Task OpenPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        StopSlideshow();
        CancelWork();
        IsLoading = true;
        HasError = false;

        ImageSource source;

        try
        {
            source = await Task.Run(
                () => ImageSourceResolver.Resolve(path, _services.Settings.SortDescending));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(ImageLoadFailure.NotFound, null);
            return;
        }
        catch (FileNotFoundException)
        {
            ShowError(ImageLoadFailure.NotFound, null);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ShowError(ImageLoadFailure.AccessDenied, null);
            return;
        }
        catch (IOException)
        {
            ShowError(ImageLoadFailure.Unknown, null);
            return;
        }

        if (source.Files.Count == 0)
        {
            ApplyFolder(source.FolderPath, source.Files, 0);
            ShowFolderEmpty();
            IsLoading = false;
            return;
        }

        ApplyFolder(source.FolderPath, source.Files, source.Index);
        IsLoading = false;
        await NavigateToIndexAsync(source.Index, navigation: true, animate: false);
    }

    public async Task OpenDroppedPathAsync(string path)
    {
        if (Directory.Exists(path))
        {
            await OpenPathAsync(path);
            return;
        }

        if (!File.Exists(path))
        {
            ShowError(ImageLoadFailure.NotFound, Path.GetExtension(path));
            return;
        }

        if (!SupportedFormats.IsSupported(path))
        {
            ShowError(ImageLoadFailure.Unsupported, Path.GetExtension(path));
            return;
        }

        await OpenPathAsync(path);
    }

    public async Task NextAsync()
    {
        var step = _navigator.MoveNext();
        if (step is null)
        {
            return;
        }

        if (step.Value.Wrapped)
        {
            ShowMessage(_services.Localization.T("L.WrappedFirst"));
        }

        await NavigateToIndexAsync(step.Value.Index, navigation: true, animate: true);
    }

    public async Task PreviousAsync()
    {
        var step = _navigator.MovePrevious();
        if (step is null)
        {
            return;
        }

        if (step.Value.Wrapped)
        {
            ShowMessage(_services.Localization.T("L.WrappedLast"));
        }

        await NavigateToIndexAsync(step.Value.Index, navigation: true, animate: true);
    }

    public async Task GoToAsync(int index)
    {
        if (index < 0 || index >= Images.Count || index == _navigator.CurrentIndex)
        {
            return;
        }

        await NavigateToIndexAsync(index, navigation: true, animate: true);
    }

    public void SetViewport(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        var changed = Math.Abs(pixelWidth - _viewportWidth) > 48 || Math.Abs(pixelHeight - _viewportHeight) > 48;
        _viewportWidth = pixelWidth;
        _viewportHeight = pixelHeight;

        if (!changed || _currentPath is null || _rawDecoded is null || _fullLoaded)
        {
            return;
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    public void OnZoomChanged(double percent)
    {
        ZoomPercent = percent;
        ZoomText = Math.Round(percent, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

        if (_rawDecoded is { IsProxy: true } proxy)
        {
            if (_regionActive)
            {
                if (percent < RegionActivationPercent)
                {
                    // We are back at a proxy-scale view. Tile work for the old
                    // viewport may still be finishing; do not leave the centered
                    // loading prompt on screen after leaving region scale.
                    _regionLoading = false;
                    _regionCacheProgress = 0;
                    ShowRegionLoading = false;
                    UpdateRegionLoadingText();
                }

                return;
            }

            if (percent >= RegionActivationPercent)
            {
                if (!_isRegionOpening)
                {
                    _ = OpenRegionAsync();
                }

                // Above the activation threshold the region tiles take over; do
                // not spend a full preview decode on an 8192 proxy as well.
                return;
            }

            if (!_isUpgradingProxy &&
                proxy.Width < ProxyMaximumEdge &&
                percent / 100.0 > proxy.Scale * 1.15)
            {
                _ = UpgradeProxyAsync();
            }

            return;
        }

        if (_rawDecoded is { IsProxy: false } raw &&
            !_fullLoaded &&
            !_isLoadingFull &&
            raw.Scale < 0.99 &&
            percent / 100.0 > raw.Scale * 1.15)
        {
            _ = LoadFullResolutionAsync();
        }
    }

    private async Task UpgradeProxyAsync()
    {
        if (_isUpgradingProxy)
        {
            return;
        }

        _isUpgradingProxy = true;
        try
        {
            // Coalesce a wheel/gesture burst and decode the final tier once.
            await Task.Delay(500, _lifetimeCts.Token);
            while (true)
            {
                var path = _currentPath;
                var metadata = _metadata;
                var current = _rawDecoded;
                if (path is null || metadata is null || current is not { IsProxy: true })
                {
                    break;
                }

                var target = NextProxyEdge(metadata);
                if (target <= current.Width * 1.05 || target > ProxyMaximumEdge)
                {
                    break;
                }

                var version = _loadVersion;
                _proxyCts?.Cancel();
                _proxyCts?.Dispose();
                _proxyCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                var token = _proxyCts.Token;
                var decoded = await _loader.DecodePreviewAsync(path, target, target, token);
                token.ThrowIfCancellationRequested();
                if (version != _loadVersion ||
                    !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // Region tiles were published while this larger proxy was being
                // decoded. Replacing the viewer image now would clear the tile
                // source and leave the canvas stuck on a blurry proxy.
                if (_regionActive || _isRegionOpening)
                {
                    break;
                }

                _loader.Store(path, target, target, decoded);
                _rawDecoded = decoded;
                var edit = _edit;
                var display = edit.IsIdentity ? decoded : ImageTransform.Apply(decoded, edit);
                Present(display, metadata, resetView: false, animate: false, isNavigation: false);
                UpdateStatus(metadata);
            }
        }
        catch (OperationCanceledException)
        {
            // Navigation or shutdown superseded the upgrade.
        }
        catch
        {
            // Keep the current proxy if a larger one cannot be generated.
        }
        finally
        {
            _isUpgradingProxy = false;
            _proxyCts?.Dispose();
            _proxyCts = null;
        }
    }

    private int NextProxyEdge(ImageMetadata metadata)
    {
        var needed = (int)Math.Ceiling(
            metadata.DisplayWidth * (Math.Max(1, ZoomPercent) / 100.0) * 1.15);
        if (needed <= 2048)
        {
            return 2048;
        }

        return needed <= 4096 ? 4096 : ProxyMaximumEdge;
    }

    private async Task OpenRegionAsync()
    {
        if (_isRegionOpening || _regionActive)
        {
            return;
        }

        var path = _currentPath;
        var metadata = _metadata;
        if (path is null || metadata is null)
        {
            return;
        }

        _isRegionOpening = true;
        _proxyCts?.Cancel();
        _loadCts?.Cancel();
        var version = _loadVersion;
        try
        {
            var tiles = await Task.Run(
                () => TiledImageSource.OpenRegion(
                    path,
                    metadata.DisplayWidth,
                    metadata.DisplayHeight),
                _lifetimeCts.Token);
            if (version != _loadVersion ||
                !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                tiles.Dispose();
                return;
            }

            _regionActive = true;
            _regionLoading = false;
            _regionCacheProgress = 0;
            ShowRegionLoading = false;
            UpdateRegionLoadingText();
            PublishTiled(tiles);
            UpdateStatus(metadata);
        }
        catch (OperationCanceledException)
        {
            // Navigation or shutdown superseded the region source.
        }
        catch
        {
            // Keep the proxy preview when the region source cannot be opened.
        }
        finally
        {
            _isRegionOpening = false;
        }
    }

    public async Task RotateClockwiseAsync()
    {
        await ApplyEditAsync(_edit.RotateClockwise());
    }

    public async Task RotateCounterClockwiseAsync()
    {
        await ApplyEditAsync(_edit.RotateCounterClockwise());
    }

    public async Task FlipHorizontalAsync()
    {
        await ApplyEditAsync(_edit.FlipHorizontal());
    }

    public async Task FlipVerticalAsync()
    {
        await ApplyEditAsync(_edit.FlipVertical());
    }

    public bool BeginEdit()
    {
        if (IsEditing)
        {
            return true;
        }

        if (!CanEdit)
        {
            ShowMessage(_services.Localization.T(
                IsProxyOnly ? "L.EditNeedsFullResolution" : "L.EditUnavailable"));
            return false;
        }

        _editAtSessionStart = _edit;
        _crop = CropRegion.Full;
        CloseAnalysis();
        IsEditing = true;
        return true;
    }

    public void SetCrop(CropRegion crop)
    {
        _crop = crop.Clamp();
        OnPropertyChanged(nameof(Crop));
    }

    public async Task ResetEditAsync()
    {
        _crop = CropRegion.Full;
        OnPropertyChanged(nameof(Crop));
        await ApplyEditAsync(EditTransform.None);
    }

    public async Task CancelEditAsync()
    {
        var original = _editAtSessionStart;
        _crop = CropRegion.Full;
        OnPropertyChanged(nameof(Crop));
        await ApplyEditAsync(original);
        IsEditing = false;
    }

    public async Task FinishEditAsync()
    {
        var original = _editAtSessionStart;
        _crop = CropRegion.Full;
        OnPropertyChanged(nameof(Crop));
        await ApplyEditAsync(original);
        IsEditing = false;
    }

    public async Task<string> ExportEditedAsync(
        string destinationPath,
        EditExportOptions options,
        CancellationToken cancellationToken)
    {
        var path = _currentPath ?? throw new InvalidOperationException("No image is open.");
        var target = ImageExporter.NextAvailablePath(destinationPath);
        var transform = _edit;
        var crop = _crop.Clamp();

        await Task.Run(
            () => ExportManagedFallback(path, transform, crop, options, target, cancellationToken),
            cancellationToken);

        ShowMessage(_services.Localization.Format("L.EditExportSucceeded", target));
        return target;
    }

    private static void ExportManagedFallback(
        string sourcePath,
        EditTransform transform,
        CropRegion crop,
        EditExportOptions options,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var full = ImageDecoder.DecodeFull(sourcePath, cancellationToken);
        var edited = transform.IsIdentity ? full : ImageTransform.Apply(full, transform);
        var bytes = ImageExporter.EncodeEdited(edited, crop, options);
        ImageExporter.WriteNewFile(destinationPath, bytes);
    }

    public void ToggleInfo()
    {
        if (!InfoVisible)
        {
            FilmstripVisible = false;
            CloseAnalysis();
        }

        InfoVisible = !InfoVisible;
    }

    public void ToggleFilmstrip()
    {
        if (!FilmstripVisible)
        {
            InfoVisible = false;
            CloseAnalysis();
        }

        FilmstripVisible = !FilmstripVisible;
    }

    public void OpenAnalysis(AnalysisTab tab)
    {
        if (!HasImage || IsEditing || IsLoading)
        {
            return;
        }

        Analysis.SelectTab(tab);
        if (!AnalysisVisible)
        {
            InfoVisible = false;
            FilmstripVisible = false;
            AnalysisVisible = true;
            StopSlideshow();
        }

        RefreshAnalysis();
    }

    public void CloseAnalysis()
    {
        if (!AnalysisVisible)
        {
            return;
        }

        AnalysisVisible = false;
        Analysis.SetEyedropper(false);
        Analysis.ClearSamples();
        Analysis.Cancel();
    }

    public void RefreshAnalysis()
    {
        if (!AnalysisVisible)
        {
            return;
        }

        if (_currentPath is null || _metadata is null || _rawDecoded is null || _displayDecoded is null)
        {
            Analysis.SetUnavailable();
            return;
        }

        var frameCount = _currentAnimation?.FrameCount ?? 1;
        var duration = _currentAnimation is null
            ? 0
            : _currentAnimation.DelaysMilliseconds.Sum() / 1000.0;
        var loop = _currentAnimation?.RepetitionCount ?? 1;
        _ = Analysis.LoadAsync(
            new AnalysisRequest(
                _currentPath,
                _rawDecoded,
                _displayDecoded,
                _metadata,
                frameCount,
                duration,
                loop));
    }

    public bool TryGetDisplayedPixel(int x, int y, out RgbColor color, out byte alpha)
    {
        color = default;
        alpha = 0;
        var image = _displayDecoded;
        if (image is null || x < 0 || y < 0 || x >= image.Width || y >= image.Height)
        {
            return false;
        }

        var offset = y * image.Stride + x * 4;
        var b = image.Pixels[offset];
        var g = image.Pixels[offset + 1];
        var r = image.Pixels[offset + 2];
        alpha = image.Pixels[offset + 3];
        if (alpha != 0 && alpha != 255)
        {
            b = Unpremultiply(b, alpha);
            g = Unpremultiply(g, alpha);
            r = Unpremultiply(r, alpha);
        }

        color = new RgbColor(r, g, b);
        return true;
    }

    public void SetFullscreen(bool fullscreen) => IsFullscreen = fullscreen;

    public void ToggleSlideshow()
    {
        if (IsSlideshowRunning)
        {
            StopSlideshow();
        }
        else
        {
            StartSlideshow();
        }
    }

    public void StartSlideshow()
    {
        if (!HasImage)
        {
            ShowMessage(_services.Localization.T("L.NoImage"));
            return;
        }

        _slideshowTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_services.Settings.SlideshowSeconds, 3, 10));
        _slideshowTimer.Start();
        IsSlideshowRunning = true;
        ShowMessage(_services.Localization.T("L.SlideshowRunning"));
    }

    public void StopSlideshow()
    {
        if (!IsSlideshowRunning)
        {
            return;
        }

        _slideshowTimer.Stop();
        IsSlideshowRunning = false;
        TransientMessage = null;
    }

    public void ShowMessage(string message)
    {
        TransientMessage = message;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    public async Task ApplySortAsync()
    {
        if (_currentPath is null || Images.Count == 0)
        {
            return;
        }

        var currentPath = _currentPath;
        var folder = Path.GetDirectoryName(currentPath);
        if (folder is null)
        {
            return;
        }

        try
        {
            var files = await Task.Run(
                () => FolderScanner.EnumerateImages(folder, _services.Settings.SortDescending));
            if (files.Count == 0)
            {
                return;
            }

            var target = files.ToList().FindIndex(
                path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
            if (target < 0)
            {
                target = 0;
            }

            ApplyFolder(folder, files, target);
            await NavigateToIndexAsync(target, navigation: true, animate: false, forceReload: true);
        }
        catch
        {
            // A sort change must not break the current view.
        }
    }

    public async Task<byte[]> EncodeCurrentAsync(ExportFormat format, CancellationToken cancellationToken)
    {
        var path = _currentPath ?? throw new InvalidOperationException("No image is open.");
        var edit = _edit;

        return await Task.Run(
            () =>
            {
                var full = ImageDecoder.DecodeFull(path, cancellationToken);
                var display = edit.IsIdentity ? full : ImageTransform.Apply(full, edit);
                return ImageExporter.Encode(display, format);
            },
            cancellationToken);
    }

    public async Task<string?> SaveCopyAsync(string requestedPath, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            var written = await Task.Run(
                () => ImageExporter.WriteNewFile(requestedPath, bytes),
                cancellationToken);
            ShowMessage(_services.Localization.Format("L.SaveSucceeded", written));
            return written;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            ShowMessage(_services.Localization.T("L.SaveFailed"));
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        CancelWork();
        _thumbnails?.Dispose();
        _thumbnails = null;
        _messageTimer.Stop();
        _slideshowTimer.Stop();
        _resizeTimer.Stop();
        _services.Localization.LanguageChanged -= OnLanguageChanged;
        _services.SettingsChanged -= OnSettingsChanged;
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        _currentAnimation?.Dispose();
        _currentAnimation = null;
        Analysis.Dispose();
        DisposeTiled();
    }

    private async Task NavigateToIndexAsync(int index, bool navigation, bool animate, bool forceReload = false)
    {
        if (index < 0 || index >= _navigator.Count)
        {
            return;
        }

        _navigator.GoTo(index);
        CurrentIndex = index;
        CurrentItem = Images[index];
        _currentPath = _navigator.CurrentPath;

        if (!forceReload && _rawDecoded is not null && _metadata is not null &&
            string.Equals(_metadata.FilePath, _currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await LoadCurrentAsync(navigation, animate);
        CurrentItemChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadCurrentAsync(bool navigation, bool animate)
    {
        var path = _currentPath;
        if (path is null)
        {
            ShowEmptyState();
            return;
        }

        var version = ++_loadVersion;
        CancelLoadTokens();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;

        IsEditing = false;
        _crop = CropRegion.Full;
        IsLoading = true;
        HasError = false;
        _fullLoaded = false;
        _regionActive = false;
        _isRegionOpening = false;
        _regionCacheProgress = 0;
        ShowRegionLoading = false;
        UpdateRegionLoadingText();
        _edit = EditTransform.None;
        _editVersion++;

        try
        {
            var preload = await TakePreloadAsync(path);
            var metadata = preload?.Metadata ?? await GetMetadataAsync(path, cancellationToken);
            _metadataCache[path] = metadata;
            var (targetWidth, targetHeight) = PreviewTarget();

            // An animated GIF is decoded as a frame set; the ordinary preview cache
            // only holds the first frame and must not replace the animation.
            if (metadata.IsAnimated)
            {
                var animationStopwatch = Stopwatch.StartNew();
                var animation = await _loader.DecodeAnimationAsync(
                    path,
                    targetWidth,
                    targetHeight,
                    cancellationToken);
                animationStopwatch.Stop();
                if (animation is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (version != _loadVersion)
                    {
                        return;
                    }

                    _metadata = metadata;
                    LastDecodeMilliseconds = animationStopwatch.Elapsed.TotalMilliseconds;
                    PresentAnimation(animation, metadata, resetView: navigation, animate: navigation && animate);
                    UpdateStatus(metadata);
                    RebuildInfo();

                    if (_thumbnailsPending)
                    {
                        _thumbnailsPending = false;
                        _thumbnails?.Start(Images, _lifetimeCts.Token);
                    }

                    StartPrefetch(version, targetWidth, targetHeight);
                    return;
                }
            }

            DecodedBitmap? decoded = null;
            var decodeMilliseconds = 0.0;

            if (preload is not null)
            {
                decoded = preload.Bitmap;
                decodeMilliseconds = preload.DecodeMilliseconds;
                _loader.Store(path, targetWidth, targetHeight, decoded);
            }
            else if (_loader.TryGet(path, targetWidth, targetHeight, out var cached))
            {
                decoded = cached;
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                decoded = await _loader.DecodePreviewAsync(path, targetWidth, targetHeight, cancellationToken);
                stopwatch.Stop();
                decodeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                _loader.Store(path, targetWidth, targetHeight, decoded);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (version != _loadVersion)
            {
                return;
            }

            _rawDecoded = decoded;
            _metadata = metadata;
            LastDecodeMilliseconds = decodeMilliseconds;

            Present(decoded, metadata, resetView: navigation, animate: navigation && animate, isNavigation: navigation);
            UpdateStatus(metadata);
            RebuildInfo();

            if (_thumbnailsPending)
            {
                _thumbnailsPending = false;
                _thumbnails?.Start(Images, _lifetimeCts.Token);
            }

            StartPrefetch(version, targetWidth, targetHeight);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer navigation.
        }
        catch (ImageLoadException exception)
        {
            if (version == _loadVersion)
            {
                ShowError(exception.Failure, exception.Extension);
            }
        }
        catch
        {
            if (version == _loadVersion)
            {
                ShowError(ImageLoadFailure.Unknown, Path.GetExtension(path));
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadFullResolutionAsync()
    {
        var path = _currentPath;
        if (path is null || _fullLoaded || _isLoadingFull || _currentAnimation is not null ||
            _rawDecoded?.IsProxy == true)
        {
            return;
        }

        _isLoadingFull = true;
        _fullCts?.Cancel();
        _fullCts?.Dispose();
        _fullCts = new CancellationTokenSource();
        var cancellationToken = _fullCts.Token;
        var version = _loadVersion;

        try
        {
            var huge = _metadata is { } metadata &&
                       (long)metadata.DisplayWidth * metadata.DisplayHeight >= TiledPixelThreshold;
            if (huge && _edit.IsIdentity)
            {
                DecodedImageBuffer? raw = null;
                try
                {
                    raw = await Task.Run(
                        () => ImageDecoder.DecodeFullRaw(path, cancellationToken),
                        cancellationToken);
                    if (version != _loadVersion ||
                        !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var tiles = await TiledImageSource.CreateAsync(raw, cancellationToken);
                    if (version != _loadVersion ||
                        !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase) ||
                        !_edit.IsIdentity)
                    {
                        tiles.Dispose();
                    }
                    else
                    {
                        _fullLoaded = true;
                        PublishTiled(tiles);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // A tile-cache failure falls back to the ordinary full bitmap.
                }
                finally
                {
                    raw?.Dispose();
                }
            }

            var full = await _loader.DecodeFullAsync(path, cancellationToken);
            if (version != _loadVersion || !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var edit = _edit;
            var display = edit.IsIdentity
                ? full
                : await Task.Run(() => ImageTransform.Apply(full, edit), cancellationToken);

            if (version != _loadVersion)
            {
                return;
            }

            if (edit != _edit)
            {
                var currentEdit = _edit;
                display = currentEdit.IsIdentity
                    ? full
                    : await Task.Run(() => ImageTransform.Apply(full, currentEdit), cancellationToken);
            }

            if (_regionActive)
            {
                return;
            }

            _rawDecoded = full;
            _fullLoaded = true;
            Present(display, _metadata, resetView: false, animate: false, isNavigation: false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by navigation.
        }
        catch
        {
            // The preview stays usable when a full-resolution decode fails.
        }
        finally
        {
            _isLoadingFull = false;
        }
    }

    private void PublishTiled(TiledImageSource tiles)
    {
        var old = _tiledSource;
        if (old is not null)
        {
            old.TileLoadingChanged -= OnRegionTileLoadingChanged;
            old.RegionProgressChanged -= OnRegionProgressChanged;
        }

        _tiledSource = tiles;
        tiles.TileLoadingChanged += OnRegionTileLoadingChanged;
        tiles.RegionProgressChanged += OnRegionProgressChanged;
        _regionCacheProgress = tiles.RegionProgress;
        UpdateRegionLoadingText();
        TiledImagePresented?.Invoke(this, new TiledImagePresentedEventArgs(tiles));
        old?.Dispose();
    }

    private void OnRegionProgressChanged(object? sender, EventArgs e)
    {
        var tiles = _tiledSource;
        if (!_regionActive || tiles is null || !ReferenceEquals(sender, tiles))
        {
            return;
        }

        _regionCacheProgress = tiles.RegionProgress;
        if (_regionCacheProgress is > 0 and < 1)
        {
            ShowRegionLoading = true;
        }
        else if (!_regionLoading)
        {
            ShowRegionLoading = false;
        }

        UpdateRegionLoadingText();
        if (_metadata is { } metadata)
        {
            UpdateStatus(metadata);
        }
    }

    private void UpdateRegionLoadingText()
    {
        var progress = Math.Clamp(_regionCacheProgress, 0, 1);
        RegionLoadingText = progress is > 0 and < 1
            ? $"{_services.Localization.T("L.CachingRegion")} {progress * 100:0}%"
            : _services.Localization.T("L.LoadingRegion");
    }

    private void OnRegionTileLoadingChanged(object? sender, EventArgs e)
    {
        if (!_regionActive || _metadata is not { } metadata)
        {
            return;
        }

        if (ZoomPercent < RegionActivationPercent)
        {
            _regionLoading = false;
            _regionCacheProgress = 0;
            ShowRegionLoading = false;
            UpdateRegionLoadingText();
            UpdateStatus(metadata);
            return;
        }

        _regionLoading = _tiledSource?.PendingTileCount > 0;
        ShowRegionLoading = _regionLoading || _regionCacheProgress is > 0 and < 1;
        UpdateRegionLoadingText();
        UpdateStatus(metadata);
    }

    private void DisposeTiled()
    {
        var old = _tiledSource;
        if (old is not null)
        {
            old.TileLoadingChanged -= OnRegionTileLoadingChanged;
            old.RegionProgressChanged -= OnRegionProgressChanged;
        }

        _tiledSource = null;
        _regionCacheProgress = 0;
        TiledImageCleared?.Invoke(this, EventArgs.Empty);
        old?.Dispose();
    }

    private async Task ApplyEditAsync(EditTransform transform)
    {
        if (_tiledSource is not null && _currentPath is not null)
        {
            await ApplyTiledEditAsync(transform);
            return;
        }

        if (_rawDecoded is null || _currentPath is null)
        {
            return;
        }

        _edit = transform;
        var version = ++_editVersion;
        var path = _currentPath;
        var raw = _rawDecoded;

        try
        {
            var display = await Task.Run(() => ImageTransform.Apply(raw, transform));
            if (version != _editVersion || !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (AnalysisVisible)
            {
                Analysis.ClearSamples();
            }

            Present(display, _metadata, resetView: false, animate: false, isNavigation: false);
            if (IsEditing)
            {
                _crop = CropRegion.Full;
                OnPropertyChanged(nameof(Crop));
            }
        }
        catch
        {
            // Editing is display-only; a failure leaves the previous pixels in place.
        }
    }

    private async Task ApplyTiledEditAsync(EditTransform transform)
    {
        var path = _currentPath;
        if (path is null)
        {
            return;
        }

        var version = ++_editVersion;
        var previous = _edit;
        _edit = transform;
        DecodedImageBuffer? raw = null;
        try
        {
            raw = await Task.Run(
                () => ImageDecoder.DecodeFullRaw(path, _lifetimeCts.Token),
                _lifetimeCts.Token);
            var tiles = await TiledImageSource.CreateAsync(
                raw,
                transform,
                _lifetimeCts.Token);
            if (version != _editVersion ||
                !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                tiles.Dispose();
                return;
            }

            _fullLoaded = true;
            PublishTiled(tiles);
            StatusDimensions = string.Format(
                CultureInfo.InvariantCulture,
                "{0} \u00D7 {1}",
                tiles.PixelWidth,
                tiles.PixelHeight);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit or navigation.
        }
        catch
        {
            _edit = previous;
        }
        finally
        {
            raw?.Dispose();
        }
    }

    private void PresentAnimation(
        DecodedAnimation animation,
        ImageMetadata metadata,
        bool resetView,
        bool animate)
    {
        var oldTiles = _tiledSource;
        if (oldTiles is not null)
        {
            oldTiles.TileLoadingChanged -= OnRegionTileLoadingChanged;
            oldTiles.RegionProgressChanged -= OnRegionProgressChanged;
        }

        _tiledSource = null;
        _regionActive = false;
        _regionLoading = false;
        _regionCacheProgress = 0;
        ShowRegionLoading = false;
        UpdateRegionLoadingText();
        IsEditing = false;
        _crop = CropRegion.Full;
        var oldBitmap = _currentBitmap;
        var oldAnimation = _currentAnimation;

        var frames = new List<WriteableBitmap>(animation.FrameCount);
        try
        {
            foreach (var frame in animation.Frames)
            {
                frames.Add(BitmapFactory.FromDecoded(frame));
            }

            var source = animation.Frames[0];
            var animatedImage = new AnimatedImage(
                frames,
                animation.DelaysMilliseconds,
                source.SourceWidth,
                source.SourceHeight,
                source.Scale,
                animation.RepetitionCount);

            _currentBitmap = null;
            _currentAnimation = animatedImage;
            _rawDecoded = source;
            _displayDecoded = source;
            _metadata = metadata;
            HasImage = true;
            HasError = false;
            OverlayVisible = false;
            _folderEmpty = false;

            TitleFileName = metadata.FileName;
            WindowTitle = FormatWindowTitle();
            StatusDimensions = string.Format(
                CultureInfo.InvariantCulture,
                "{0} × {1}",
                source.SourceWidth,
                source.SourceHeight);

            ImagePresented?.Invoke(
                this,
                new ImagePresentedEventArgs(
                    null,
                    source.SourceWidth,
                    source.SourceHeight,
                    source.Scale,
                    resetView,
                    animate,
                    true,
                    animatedImage));
        }
        catch
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }

        oldBitmap?.Dispose();
        oldAnimation?.Dispose();
        oldTiles?.Dispose();
        RefreshAnalysis();
    }

    private void Present(
        DecodedBitmap display,
        ImageMetadata? metadata,
        bool resetView,
        bool animate,
        bool isNavigation)
    {
        var oldTiles = _tiledSource;
        _tiledSource = null;
        var oldAnimation = _currentAnimation;
        _currentAnimation = null;
        var bitmap = BitmapFactory.FromDecoded(display);
        var old = _currentBitmap;
        _currentBitmap = bitmap;
        if (AnalysisVisible &&
            _displayDecoded is not null &&
            (_displayDecoded.Width != display.Width || _displayDecoded.Height != display.Height))
        {
            Analysis.ClearSamples();
        }

        _displayDecoded = display;
        HasImage = true;
        HasError = false;
        OverlayVisible = false;
        _folderEmpty = false;

        TitleFileName = metadata?.FileName ?? Path.GetFileName(_currentPath) ?? string.Empty;
        WindowTitle = FormatWindowTitle();
        StatusDimensions = string.Format(
            CultureInfo.InvariantCulture,
            "{0} \u00D7 {1}",
            display.SourceWidth,
            display.SourceHeight);

        ImagePresented?.Invoke(
            this,
            new ImagePresentedEventArgs(
                bitmap,
                display.SourceWidth,
                display.SourceHeight,
                display.Scale,
                resetView,
                animate,
                isNavigation));

        old?.Dispose();
        oldAnimation?.Dispose();
        oldTiles?.Dispose();
        RefreshAnalysis();
    }

    private async Task<ImageMetadata> GetMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (_metadataCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var metadata = await Task.Run(() => ImageDecoder.ReadMetadata(path), cancellationToken);
        if (_metadataCache.Count > 64)
        {
            _metadataCache.Clear();
        }

        _metadataCache[path] = metadata;
        return metadata;
    }

    private async Task<PreloadedImage?> TakePreloadAsync(string path)
    {
        var task = _startupPreload;
        _startupPreload = null;
        if (task is null)
        {
            return null;
        }

        try
        {
            var preload = await task;
            return preload is not null &&
                   string.Equals(preload.Path, path, StringComparison.OrdinalIgnoreCase)
                ? preload
                : null;
        }
        catch
        {
            return null;
        }
    }

    private (int Width, int Height) PreviewTarget()
    {
        var width = Math.Max(640, _viewportWidth);
        var height = Math.Max(480, _viewportHeight);
        return (Math.Min(width, 8192), Math.Min(height, 8192));
    }

    private void StartPrefetch(int version, int targetWidth, int targetHeight)
    {
        if (_navigator.Count < 2)
        {
            return;
        }

        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = new CancellationTokenSource();
        var cancellationToken = _prefetchCts.Token;
        var index = _navigator.CurrentIndex;
        var files = _navigator.Files.ToArray();

        _ = Task.Run(
            () =>
            {
                foreach (var offset in new[] { 1, -1 })
                {
                    if (cancellationToken.IsCancellationRequested || version != _loadVersion)
                    {
                        return;
                    }

                    var neighbour = files[(index + offset + files.Length) % files.Length];
                    if (_loader.TryGet(neighbour, targetWidth, targetHeight, out _))
                    {
                        continue;
                    }

                    try
                    {
                        var bitmap = ImageDecoder.Decode(neighbour, targetWidth, targetHeight, cancellationToken);
                        if (!cancellationToken.IsCancellationRequested && version == _loadVersion)
                        {
                            _loader.Store(neighbour, targetWidth, targetHeight, bitmap);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch
                    {
                        // Prefetch is an optimisation; failures stay silent.
                    }
                }
            },
            CancellationToken.None);
    }

    private void ApplyFolder(string folder, IReadOnlyList<string> files, int index)
    {
        _navigator.Reset(files, index);
        Images.Clear();

        foreach (var file in files)
        {
            Images.Add(new ImageItemViewModel(file));
        }

        CurrentIndex = index >= 0 && index < Images.Count ? index : 0;
        CurrentItem = Images.Count > 0 ? Images[CurrentIndex] : null;
        OnPropertyChanged(nameof(FilmstripSummary));
        for (var itemIndex = 0; itemIndex < Images.Count; itemIndex++)
        {
            Images[itemIndex].IsCurrent = itemIndex == CurrentIndex;
        }

        _currentPath = _navigator.CurrentPath;
        _folderEmpty = files.Count == 0;
        FolderName = DescribeFolder(folder);
        _lastFailure = null;
        _lastFailureExtension = null;
        OverlayVisible = false;
        HasError = false;
        _metadataCache.Clear();
        _loader.Clear();

        _thumbnails?.Dispose();
        _thumbnails = new ThumbnailService(_services.Thumbnails);
        _thumbnailsPending = true;

        StatusPosition = files.Count == 0 ? string.Empty : $"{CurrentIndex + 1}/{files.Count}";
    }

    private void ShowEmptyState()
    {
        ClearPresentedImage();
        HasError = false;
        _folderEmpty = false;
        _lastFailure = null;
        _lastFailureExtension = null;
        OverlayVisible = true;
        FolderName = string.Empty;
        OverlayTitle = _services.Localization.T("L.EmptyTitle");
        OverlayMessage = string.Empty;
        OverlayHint = _services.Localization.T("L.EmptyLimitations");
        TitleFileName = string.Empty;
        WindowTitle = FormatWindowTitle();
        StatusDimensions = string.Empty;
        StatusPosition = string.Empty;
        InfoSections = [];
    }

    private static string DescribeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? folder : name;
    }

    /// <summary>
    /// The window caption. <c>L.WindowTitleFormat</c> owns the separator, so no call
    /// site has to repeat the em dash (and none can corrupt it).
    /// </summary>
    private string FormatWindowTitle() =>
        string.IsNullOrEmpty(TitleFileName)
            ? _services.Localization.T("L.AppName")
            : _services.Localization.Format("L.WindowTitleFormat", TitleFileName);

    private void ShowFolderEmpty()
    {
        ClearPresentedImage();
        HasError = false;
        _folderEmpty = true;
        _lastFailure = null;
        OverlayVisible = true;
        OverlayTitle = _services.Localization.T("L.EmptyFolderTitle");
        OverlayMessage = string.Empty;
        OverlayHint = _services.Localization.T("L.EmptyFolderHint");
        TitleFileName = string.Empty;
        WindowTitle = FormatWindowTitle();
        StatusDimensions = string.Empty;
        StatusPosition = string.Empty;
        InfoSections = [];
    }

    private void ShowError(ImageLoadFailure failure, string? extension)
    {
        ClearPresentedImage();
        _lastFailure = failure;
        _lastFailureExtension = extension;
        HasError = true;
        OverlayVisible = true;
        OverlayTitle = _services.Localization.T("L.CantOpenTitle");
        OverlayMessage = LocalizeFailure(failure, extension);
        OverlayHint = failure == ImageLoadFailure.TooLarge
            ? _services.Localization.T("L.TooLargeHint")
            : _services.Localization.T("L.EmptyLimitations");
        TitleFileName = _currentPath is null ? string.Empty : Path.GetFileName(_currentPath) ?? string.Empty;
        WindowTitle = FormatWindowTitle();
        _rawDecoded = null;
        _metadata = null;
        _folderEmpty = false;
        StatusDimensions = string.Empty;
        InfoSections = [];
        IsLoading = false;
    }

    private string LocalizeFailure(ImageLoadFailure failure, string? extension)
    {
        var localization = _services.Localization;

        return failure switch
        {
            ImageLoadFailure.Unsupported => localization.Format(
                "L.ErrorUnsupportedFormat",
                string.IsNullOrWhiteSpace(extension) ? string.Empty : extension),
            ImageLoadFailure.NotFound => localization.T("L.ErrorNotFound"),
            ImageLoadFailure.TooLarge => localization.T("L.ErrorTooLarge"),
            ImageLoadFailure.AccessDenied => localization.T("L.ErrorAccessDenied"),
            ImageLoadFailure.Corrupt => localization.T("L.ErrorCorrupt"),
            _ => localization.T("L.ErrorUnknown")
        };
    }

    private void ClearPresentedImage()
    {
        var old = _currentBitmap;
        _currentBitmap = null;
        var oldAnimation = _currentAnimation;
        _currentAnimation = null;
        var oldTiles = _tiledSource;
        _tiledSource = null;
        HasImage = false;
        _rawDecoded = null;
        _displayDecoded = null;
        _metadata = null;
        _edit = EditTransform.None;
        if (AnalysisVisible)
        {
            Analysis.SetUnavailable();
        }

        ImagePresented?.Invoke(
            this,
            new ImagePresentedEventArgs(null, 0, 0, 1, true, false, false));
        old?.Dispose();
        oldAnimation?.Dispose();
        oldTiles?.Dispose();
    }

    private void UpdateStatus(ImageMetadata metadata)
    {
        var dimensions = string.Format(
            CultureInfo.InvariantCulture,
            "{0} \u00D7 {1}",
            metadata.DisplayWidth,
            metadata.DisplayHeight);
        StatusDimensions = _regionActive
            ? string.Concat(
                dimensions,
                " · ",
                _services.Localization.T("L.Region"))
            : _rawDecoded?.IsProxy == true
                ? string.Concat(
                    dimensions,
                    " · ",
                    _services.Localization.T("L.Preview"),
                    " ",
                    _rawDecoded.Width.ToString(CultureInfo.InvariantCulture),
                    "px")
                : dimensions;
        StatusPosition = _navigator.Count == 0
            ? string.Empty
            : $"{_navigator.CurrentIndex + 1}/{_navigator.Count}";
    }

    private void RebuildInfo()
    {
        if (_metadata is not { } metadata)
        {
            InfoSections = [];
            return;
        }

        var localization = _services.Localization;
        var sections = new List<InfoSection>();

        var fileRows = new List<InfoRow>
        {
            new(localization.T("L.InfoName"), metadata.FileName),
            new(localization.T("L.InfoPath"), metadata.FilePath),
            new(localization.T("L.InfoSize"), FormatBytes(metadata.FileSizeBytes), isMono: true),
            new(
                localization.T("L.InfoModified"),
                metadata.LastWriteTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                isMono: true)
        };
        sections.Add(new InfoSection(localization.T("L.SectionFile"), fileRows));

        var imageRows = new List<InfoRow>
        {
            new(
                localization.T("L.InfoDimensions"),
                string.Format(CultureInfo.InvariantCulture, "{0} \u00D7 {1}", metadata.DisplayWidth, metadata.DisplayHeight),
                isMono: true),
            new(localization.T("L.InfoFormat"), metadata.FormatName),
            new(localization.T("L.InfoBitDepth"), localization.Format("L.BitDepthFormat", metadata.BitDepth), isMono: true)
        };
        if (metadata.IsAnimated)
        {
            imageRows.Add(new InfoRow(
                localization.T("L.InfoFrames"),
                metadata.FrameCount.ToString(CultureInfo.InvariantCulture),
                isMono: true));
        }

        sections.Add(new InfoSection(localization.T("L.SectionImage"), imageRows));

        var colorRows = new List<InfoRow>
        {
            new(localization.T("L.InfoColorSpace"), metadata.ColorSpaceDisplay)
        };
        if (metadata.HasIccProfile)
        {
            colorRows.Add(new InfoRow(localization.T("L.InfoIcc"), metadata.IccDescription!));
        }

        sections.Add(new InfoSection(localization.T("L.SectionColor"), colorRows));

        var exif = metadata.Exif;
        var exifRows = new List<InfoRow>();
        AddRow(exifRows, localization.T("L.InfoCamera"), exif.Camera);
        AddRow(exifRows, localization.T("L.InfoLens"), exif.LensModel);
        AddRow(exifRows, localization.T("L.InfoExposure"), FormatExposure(exif.ExposureSeconds), isMono: true);
        AddRow(exifRows, localization.T("L.InfoAperture"), exif.FNumber is { } aperture ? $"f/{aperture:0.#}" : null, isMono: true);
        AddRow(exifRows, localization.T("L.InfoIso"), exif.Iso?.ToString(CultureInfo.InvariantCulture), isMono: true);
        AddRow(
            exifRows,
            localization.T("L.InfoFocal"),
            exif.FocalLengthMillimeters is { } focal ? $"{focal:0.#} mm" : null,
            isMono: true);
        AddRow(
            exifRows,
            localization.T("L.InfoTaken"),
            exif.TakenAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? exif.TakenAtRaw,
            isMono: true);

        if (exifRows.Count > 0)
        {
            sections.Add(new InfoSection(localization.T("L.SectionCamera"), exifRows));
        }

        InfoSections = sections;
    }

    private static void AddRow(List<InfoRow> rows, string label, string? value, bool isMono = false)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new InfoRow(label, value, isMono));
        }
    }

    private static string? FormatExposure(double? seconds)
    {
        if (seconds is null or <= 0)
        {
            return null;
        }

        return seconds >= 1
            ? $"{seconds:0.0} s"
            : $"1/{Math.Round(1 / seconds.Value):0} s";
    }

    private static byte Unpremultiply(byte value, byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        if (alpha == 255)
        {
            return value;
        }

        var result = (value * 255 + alpha / 2) / alpha;
        return result > 255 ? (byte)255 : (byte)result;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        if (bytes >= 1024)
        {
            return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private void OnSlideshowTick(object? sender, EventArgs e)
    {
        if (!IsSlideshowRunning || _slideshowBusy)
        {
            return;
        }

        _slideshowBusy = true;
        _ = NextAsync().ContinueWith(
            _ => _slideshowBusy = false,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnResizeTick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        if (_currentPath is null || _rawDecoded is null || _fullLoaded)
        {
            return;
        }

        if (_regionActive)
        {
            return;
        }

        var (width, height) = PreviewTarget();
        if (_rawDecoded.IsProxy)
        {
            width = Math.Max(width, _rawDecoded.Width);
            height = Math.Max(height, _rawDecoded.Height);
        }

        if (_rawDecoded.Width >= width * 0.9 && _rawDecoded.Height >= height * 0.9)
        {
            return;
        }

        _ = ReloadPreviewForViewportAsync(width, height);
    }

    private async Task ReloadPreviewForViewportAsync(int width, int height)
    {
        var path = _currentPath;
        if (path is null)
        {
            return;
        }

        var version = ++_loadVersion;
        CancelLoadTokens();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;

        try
        {
            var decoded = await _loader.DecodePreviewAsync(path, width, height, cancellationToken);
            if (version != _loadVersion || !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // A resize/reload can finish after region tiles were published.
            // Replacing the viewer here would clear the tile source.
            if (_regionActive)
            {
                return;
            }

            _loader.Store(path, width, height, decoded);
            _rawDecoded = decoded;
            var edit = _edit;
            var display = edit.IsIdentity ? decoded : ImageTransform.Apply(decoded, edit);
            Present(display, _metadata, resetView: false, animate: false, isNavigation: false);
            StartPrefetch(version, width, height);
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
        catch
        {
            // Keep the previous preview.
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_lastFailure is { } failure)
        {
            OverlayMessage = LocalizeFailure(failure, _lastFailureExtension);
            OverlayTitle = _services.Localization.T("L.CantOpenTitle");
            OverlayHint = failure == ImageLoadFailure.TooLarge
                ? _services.Localization.T("L.TooLargeHint")
                : _services.Localization.T("L.EmptyLimitations");
        }
        else if (_folderEmpty)
        {
            OverlayTitle = _services.Localization.T("L.EmptyFolderTitle");
            OverlayHint = _services.Localization.T("L.EmptyFolderHint");
        }
        else if (!HasImage)
        {
            OverlayTitle = _services.Localization.T("L.EmptyTitle");
            OverlayHint = _services.Localization.T("L.EmptyLimitations");
        }

        RebuildInfo();
        OnPropertyChanged(nameof(CacheSizeText));
        OnPropertyChanged(nameof(FilmstripTitle));
        OnPropertyChanged(nameof(CollapseFilmstripText));
        OnPropertyChanged(nameof(FilmstripSummary));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        CheckerboardEnabled = _services.Settings.Checkerboard;
        SlideshowSeconds = _services.Settings.SlideshowSeconds;

        if (IsSlideshowRunning)
        {
            _slideshowTimer.Interval = TimeSpan.FromSeconds(SlideshowSeconds);
        }

        if (!string.Equals(_appliedSort, _services.Settings.Sort, StringComparison.Ordinal))
        {
            _appliedSort = _services.Settings.Sort;
            _ = ApplySortAsync();
        }
    }

    private void CancelWork()
    {
        CancelLoadTokens();
        _thumbnails?.Stop();
    }

    private void CancelLoadTokens()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = null;
        _fullCts?.Cancel();
        _fullCts?.Dispose();
        _fullCts = null;
        _proxyCts?.Cancel();
        _proxyCts?.Dispose();
        _proxyCts = null;
        _isLoadingFull = false;
    }

    [RelayCommand]
    private Task Next() => NextAsync();

    [RelayCommand]
    private Task Previous() => PreviousAsync();

    [RelayCommand]
    private Task RotateClockwise() => RotateClockwiseAsync();

    [RelayCommand]
    private Task RotateCounterClockwise() => RotateCounterClockwiseAsync();

    [RelayCommand]
    private Task FlipHorizontal() => FlipHorizontalAsync();

    [RelayCommand]
    private Task FlipVertical() => FlipVerticalAsync();

    [RelayCommand]
    private void ToggleInfoPanel() => ToggleInfo();

    [RelayCommand]
    private void ToggleFilmstripPanel() => ToggleFilmstrip();

    partial void OnInfoVisibleChanged(bool value) => OnPropertyChanged(nameof(InfoShown));

    partial void OnAnalysisVisibleChanged(bool value) => OnPropertyChanged(nameof(AnalysisShown));

    partial void OnFilmstripVisibleChanged(bool value) => OnPropertyChanged(nameof(FilmstripShown));

    partial void OnIsFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsChromeVisible));
        OnPropertyChanged(nameof(ShowTitleBar));
        OnPropertyChanged(nameof(FilmstripShown));
        OnPropertyChanged(nameof(InfoShown));
        OnPropertyChanged(nameof(AnalysisShown));
    }

    partial void OnCurrentIndexChanged(int value)
    {
        for (var index = 0; index < Images.Count; index++)
        {
            Images[index].IsCurrent = index == value;
        }

        if (value >= 0 && value < Images.Count)
        {
            CurrentItem = Images[value];
        }

        if (!string.IsNullOrEmpty(StatusDimensions))
        {
            StatusPosition = Images.Count == 0 ? string.Empty : $"{value + 1}/{Images.Count}";
        }
    }
}




