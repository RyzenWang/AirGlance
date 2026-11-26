using System.Globalization;
using AirGlance.App.Controls;
using AirGlance.App.Imaging;
using AirGlance.App.Rendering;
using AirGlance.App.Services;
using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AirGlance.App.Views;

public sealed record DualOpenRequest(int PaneIndex, bool Folder);

public sealed record DualRevealRequest(string Path);

/// <summary>
/// Side-by-side image canvases. The view owns two independent navigation states and
/// the shared dock, filmstrip and information drawer. It deliberately uses the same
/// decoder and style vocabulary as the single-image window.
/// </summary>
public sealed partial class DualCanvasView : UserControl, IDisposable
{
    private const int MaximumPreviewWidth = 2400;

    private const int MaximumPreviewHeight = 1600;

    private readonly AppServices _services;

    private readonly PaneSlot _a;

    private readonly PaneSlot _b;

    private readonly Grid _panesGrid;

    private readonly Border _divider;

    private readonly Border _dock;

    private readonly TextBlock _zoomText;

    private readonly Button _infoButton;

    private readonly Button _filmstripButton;

    private readonly Border _filmstrip;

    private readonly FilmHalf _filmA;

    private readonly FilmHalf _filmB;

    private readonly Button _filmClose;

    private readonly Border _menuScrim;

    private readonly Border _paneMenu;

    private readonly Border _menuBadge;

    private readonly TextBlock _menuBadgeText;

    private readonly TextBlock _menuFolder;

    private readonly TextBlock _menuMeta;

    private readonly Button _menuOpenFolder;

    private readonly Button _menuOpenImage;

    private readonly Button _menuReveal;

    private readonly Button _menuCopyImage;

    private readonly Button _menuCopyPath;

    private readonly Button _menuSwap;

    private readonly Button _menuExit;

    private readonly Border _info;

    private readonly StackPanel _infoContent;

    private readonly DispatcherTimer _chromeTimer;

    private Button _zoomOutButton = null!;

    private Button _zoomInButton = null!;

    private Button _fitButton = null!;

    private Button _actualButton = null!;

    private bool _chromeVisible = true;

    private readonly Button _infoTabA;

    private readonly Button _infoTabB;

    private Border _activeBadge = null!;

    private TextBlock _activeBadgeText = null!;

    private readonly List<WriteableBitmap> _thumbnailBitmaps = [];

    private CancellationTokenSource? _filmstripCts;

    private int _filmstripVersion;

    private int _activeIndex;

    private bool _draggingDivider;

    private bool _infoVisible;

    private bool _filmstripVisible;

    private int _dragPane = -1;

    private int _menuPane;

    private bool _menuVisible;

    private readonly List<Button> _menuItems = new();

    private bool _disposed;

    public DualCanvasView(AppServices services)
    {
        _services = services;
        Focusable = true;
        ClipToBounds = true;

        _a = CreatePane(0);
        _b = CreatePane(1);
        _a.Viewer.ZoomChanged += OnPaneZoomChanged;
        _b.Viewer.ZoomChanged += OnPaneZoomChanged;
        _a.Viewer.ViewportChanged += OnViewportChanged;
        _b.Viewer.ViewportChanged += OnViewportChanged;
        _a.Host.SizeChanged += (_, _) => ApplyPaneHeaderLayout(_a);
        _b.Host.SizeChanged += (_, _) => ApplyPaneHeaderLayout(_b);

        _panesGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,1,*"),
            ClipToBounds = false
        };
        Grid.SetColumn(_a.Host, 0);
        Grid.SetColumn(_b.Host, 2);
        _panesGrid.Children.Add(_a.Host);
        _panesGrid.Children.Add(_b.Host);

        _divider = BuildDivider();
        Grid.SetColumn(_divider, 1);
        _panesGrid.Children.Add(_divider);

        var root = new Grid { ClipToBounds = true };
        root.Classes.Add("dual-root");
        root.Children.Add(_panesGrid);

        _dock = BuildDock();
        _zoomText = BuildDockCore(_dock, out _infoButton, out _filmstripButton, out _zoomText);
        root.Children.Add(_dock);

        _filmstrip = BuildFilmstrip(out _filmA, out _filmB, out _filmClose);
        root.Children.Add(_filmstrip);

        _info = BuildInfo(out _infoContent, out _infoTabA, out _infoTabB);
        root.Children.Add(_info);

        _menuScrim = BuildMenuScrim();
        root.Children.Add(_menuScrim);
        _paneMenu = BuildPaneMenu(out _menuBadge, out _menuBadgeText, out _menuFolder, out _menuMeta,
            out _menuOpenFolder, out _menuOpenImage, out _menuReveal, out _menuCopyImage,
            out _menuCopyPath, out _menuSwap, out _menuExit);
        root.Children.Add(_paneMenu);

        Content = root;
        SizeChanged += (_, _) => ApplyCompactLayout();
        ApplyCompactLayout();
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _services.Localization.LanguageChanged += OnLanguageChanged;
        _chromeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _chromeTimer.Tick += (_, _) => HideChrome();
        _chromeTimer.Start();
        SetActive(0);
    }

    public event EventHandler<DualOpenRequest>? OpenRequested;

    public event EventHandler<DualRevealRequest>? RevealRequested;

    public event EventHandler? ExitRequested;

    public int ActiveIndex => _activeIndex;

    public string? ActivePath => Pane(_activeIndex).CurrentPath;

    public string? PanePath(int paneIndex) => Pane(paneIndex).CurrentPath;

    public WriteableBitmap? ActiveBitmap =>
        Active.Bitmap ?? Active.Animation?.Frames.FirstOrDefault();

    public bool HasAnyImage => _a.CurrentPath is not null || _b.CurrentPath is not null;

    public bool HasOverlay => _infoVisible || _filmstripVisible || _menuVisible;

    public async Task InitializeAsync(string? initialPath)
    {
        ShowChrome();
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            ShowEmpty(_a);
            ShowEmpty(_b);
            return;
        }

        ImageSource source;
        try
        {
            source = await Task.Run(() => ImageSourceResolver.Resolve(initialPath));
        }
        catch
        {
            ShowEmpty(_a);
            ShowEmpty(_b);
            return;
        }

        await ApplySourceAsync(_a, source, source.Index);
        if (source.Index + 1 < source.Files.Count)
        {
            await ApplySourceAsync(_b, source with { Index = source.Index + 1 }, source.Index + 1);
        }
        else
        {
            _b.Files = source.Files.ToList();
            _b.Folder = source.FolderPath;
            _b.FileIndex = source.Index;
            ShowEmpty(_b);
        }

        SetActive(0);
    }

    public void ResetOverlays()
    {
        ClosePaneMenu();
        if (_infoVisible)
        {
            SetInfoVisible(false);
        }

        if (_filmstripVisible)
        {
            SetFilmstripVisible(false);
        }
    }

    public async Task OpenPathAsync(int paneIndex, string path)
    {
        ShowChrome();
        var pane = Pane(paneIndex);
        try
        {
            var source = await Task.Run(() => ImageSourceResolver.Resolve(path));
            await ApplySourceAsync(pane, source, source.Index);
            SetActive(paneIndex);
        }
        catch (Exception exception)
        {
            ShowError(pane, exception.Message);
        }
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        ShowChrome();
        if (_menuVisible && e.Key != Key.Escape)
        {
            ClosePaneMenu();
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            if (_menuVisible)
            {
                ClosePaneMenu();
            }
            else if (_infoVisible)
            {
                SetInfoVisible(false);
            }
            else if (_filmstripVisible)
            {
                SetFilmstripVisible(false);
            }
            else
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var host = Active.Host;
            var center = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), this)
                ?? new Point(host.Bounds.Width / 2, host.Bounds.Height / 2);
            OpenPaneMenu(_activeIndex, center);
            return true;
        }

        switch (e.Key)
        {
            case Key.D:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Key.Tab:
                SetActive(1 - _activeIndex);
                return true;
            case Key.X:
                SwapPanes();
                return true;
            case Key.Left:
            case Key.PageUp:
            case Key.Back:
                _ = NavigateAsync(-1);
                return true;
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                _ = NavigateAsync(1);
                return true;
            case Key.I:
                SetInfoVisible(!_infoVisible);
                return true;
            case Key.C:
                if (!HasAnyImage)
                {
                    return true;
                }

                SetFilmstripVisible(!_filmstripVisible);
                return true;
            case Key.OemPlus:
            case Key.Add:
                Active.Viewer.ZoomIn();
                return true;
            case Key.OemMinus:
            case Key.Subtract:
                Active.Viewer.ZoomOut();
                return true;
            case Key.D0:
            case Key.NumPad0:
                Active.Viewer.AnimateZoomMode(ViewerZoomMode.FitWindow);
                return true;
            case Key.D1:
            case Key.NumPad1:
                Active.Viewer.AnimateZoomMode(ViewerZoomMode.Actual);
                return true;
            case Key.D2:
            case Key.NumPad2:
                Active.Viewer.AnimateZoomMode(ViewerZoomMode.FitWidth);
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Localization.LanguageChanged -= OnLanguageChanged;
        _chromeTimer.Stop();
        _a.Dispose();
        _b.Dispose();
        _filmstripCts?.Cancel();
        _filmstripCts?.Dispose();
        foreach (var bitmap in _thumbnailBitmaps)
        {
            bitmap.Dispose();
        }

        _thumbnailBitmaps.Clear();
    }

    /// <summary>
    /// The decode size for one pane. The ceiling keeps the request bounded when a pane
    /// covers a very wide window or the first layout pass has not given it a size yet.
    /// </summary>
    private (int Width, int Height) PanePreviewTarget(PaneSlot pane)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (int)Math.Round(pane.Viewer.Bounds.Width * scaling);
        var height = (int)Math.Round(pane.Viewer.Bounds.Height * scaling);
        if (width <= 0 || height <= 0)
        {
            return (MaximumPreviewWidth, MaximumPreviewHeight);
        }

        return (
            Math.Clamp(width, 640, MaximumPreviewWidth),
            Math.Clamp(height, 480, MaximumPreviewHeight));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e) => ShowChrome();

    private void ShowChrome()
    {
        _chromeVisible = true;
        ApplyChrome();
        _chromeTimer.Stop();
        _chromeTimer.Start();
    }

    private void HideChrome()
    {
        if (_infoVisible || _filmstripVisible || _menuVisible || _draggingDivider)
        {
            return;
        }

        _chromeVisible = false;
        // Nothing is left to watch once the chrome has faded. Keeping the timer alive
        // only while an overlay is open (the early return above) stops a hidden dual
        // view from waking up every 2.5 s for the rest of the session.
        _chromeTimer.Stop();
        ApplyChrome();
    }

    private void ApplyChrome()
    {
        var visible = _chromeVisible;
        _a.Header.Opacity = visible ? 1 : 0;
        _b.Header.Opacity = visible ? 1 : 0;
        _a.Header.IsHitTestVisible = visible;
        _b.Header.IsHitTestVisible = visible;
        _dock.Opacity = visible ? 1 : 0;
        _dock.IsHitTestVisible = visible;
        UpdatePaneNavState(_a);
        UpdatePaneNavState(_b);
    }

    private PaneSlot Active => Pane(_activeIndex);

    private PaneSlot Pane(int index) => index == 0 ? _a : _b;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _a.EmptyTitle.Text = _services.Localization.T("L.DualEmptyTitle");
        _b.EmptyTitle.Text = _services.Localization.T("L.DualEmptyTitle");
        _a.EmptyDetail.Text = _services.Localization.T("L.DualEmptyDetail");
        _b.EmptyDetail.Text = _services.Localization.T("L.DualEmptyDetail");
    }

    private sealed class FilmHalf
    {
        public FilmHalf(int paneIndex, Border host, Border badge, TextBlock badgeText,
            TextBlock folderName, TextBlock count, ScrollViewer scroll, StackPanel thumbs)
        {
            PaneIndex = paneIndex;
            Host = host;
            Badge = badge;
            BadgeText = badgeText;
            FolderName = folderName;
            Count = count;
            Scroll = scroll;
            Thumbs = thumbs;
        }

        public int PaneIndex { get; }
        public Border Host { get; }
        public Border Badge { get; }
        public TextBlock BadgeText { get; }
        public TextBlock FolderName { get; }
        public TextBlock Count { get; }
        public ScrollViewer Scroll { get; }
        public StackPanel Thumbs { get; }
    }

    private sealed class PaneSlot : IDisposable
    {
        public PaneSlot(int index, ImageViewer viewer, Border host, Border activeFrame, StackPanel header,
            Border badge, TextBlock folderName, TextBlock folderCount, TextBlock fileName,
            TextBlock fileMeta, TextBlock fileZoom, TextBlock filePosition, Border folderChip,
            Border fileChip, Border fileSeparator1, Border fileSeparator2, Border loading,
            Border empty, TextBlock emptyTitle, TextBlock emptyDetail, Border navGroup,
            Border navBadge, Button navPrev, Button navNext)
        {
            PaneNumber = index;
            Viewer = viewer;
            Host = host;
            ActiveFrame = activeFrame;
            Header = header;
            Badge = badge;
            FolderName = folderName;
            FolderCount = folderCount;
            FileName = fileName;
            FileMeta = fileMeta;
            FileZoom = fileZoom;
            FilePosition = filePosition;
            FolderChip = folderChip;
            FileChip = fileChip;
            FileSeparator1 = fileSeparator1;
            FileSeparator2 = fileSeparator2;
            Loading = loading;
            Empty = empty;
            EmptyTitle = emptyTitle;
            EmptyDetail = emptyDetail;
            NavGroup = navGroup;
            NavBadge = navBadge;
            NavPrev = navPrev;
            NavNext = navNext;
        }

        public int PaneNumber { get; }

        public int FileIndex { get; set; }

        public ImageViewer Viewer { get; }

        public Border Host { get; }

        public Border ActiveFrame { get; }

        public StackPanel Header { get; }

        public Border Badge { get; }

        public TextBlock FolderName { get; }

        public TextBlock FolderCount { get; }

        public TextBlock FileName { get; }

        public TextBlock FileMeta { get; }

        public TextBlock FileZoom { get; }

        public TextBlock FilePosition { get; }

        public Border FolderChip { get; }

        public Border FileChip { get; }

        public Border FileSeparator1 { get; }

        public Border FileSeparator2 { get; }

        public Border Loading { get; }

        public Border Empty { get; }

        public TextBlock EmptyTitle { get; }

        public TextBlock EmptyDetail { get; }

        public Border NavGroup { get; }

        public Border NavBadge { get; }

        public Button NavPrev { get; }

        public Button NavNext { get; }

        public List<string> Files { get; set; } = [];

        public string? Folder { get; set; }

        public string? CurrentPath { get; set; }

        public ImageMetadata? Metadata { get; set; }

        public WriteableBitmap? Bitmap { get; set; }

        public AnimatedImage? Animation { get; set; }

        public string? Error { get; set; }

        public int LoadVersion { get; set; }

        public CancellationTokenSource? LoadCts { get; set; }

        public void Dispose()
        {
            LoadCts?.Cancel();
            LoadCts?.Dispose();
            Bitmap?.Dispose();
            Animation?.Dispose();
        }
    }

}
