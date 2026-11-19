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
public sealed class DualCanvasView : UserControl, IDisposable
{
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

    private PaneSlot CreatePane(int index)
    {
        var viewer = new ImageViewer
        {
            Margin = new Thickness(30, 72, 30, 68)
        };
        var host = new Border { Classes = { "dual-pane" } };
        var content = new Grid();
        content.Children.Add(viewer);

        var activeFrame = new Border { Classes = { "dual-active-frame", index == 0 ? "a" : "b" } };
        activeFrame.SetValue(Panel.ZIndexProperty, 25);
        content.Children.Add(activeFrame);

        var header = BuildHeader(index, out var badge, out var folderName, out var folderCount,
            out var fileName, out var fileMeta, out var fileZoom, out var filePosition,
            out var folderChip, out var fileChip, out var fileSeparator1, out var fileSeparator2);
        content.Children.Add(header);

        var navGroup = BuildPaneNav(index, out var navBadge, out var navPrev, out var navNext);
        content.Children.Add(navGroup);

        var loading = new Border();
        loading.Classes.Add("loading-chip");
        var loadingText = new TextBlock
        {
            Text = _services.Localization.T("L.Loading"),
            Foreground = Brushes.White,
            FontSize = 12
        };
        loading.Child = loadingText;
        loading.IsVisible = false;
        content.Children.Add(loading);

        var empty = BuildEmpty(index, out var emptyTitle, out var emptyDetail);
        content.Children.Add(empty);

        host.Child = content;
        viewer.PointerWheelChanged += (_, _) => SetActive(index);
        host.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (args.GetCurrentPoint(host).Properties.IsRightButtonPressed)
            {
                OpenPaneMenu(index, args.GetPosition(this));
                args.Handled = true;
                return;
            }

            SetActive(index);
            ShowChrome();
            Focus();
        }, RoutingStrategies.Tunnel);
        folderChip.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            OpenRequested?.Invoke(this, new DualOpenRequest(index, true));
        };

        return new PaneSlot(index, viewer, host, activeFrame, header, badge, folderName, folderCount,
            fileName, fileMeta, fileZoom, filePosition, folderChip, fileChip, fileSeparator1,
            fileSeparator2, loading, empty, emptyTitle, emptyDetail, navGroup, navBadge, navPrev,
            navNext);
    }

    private StackPanel BuildHeader(
        int index,
        out Border badge,
        out TextBlock folderName,
        out TextBlock folderCount,
        out TextBlock fileName,
        out TextBlock fileMeta,
        out TextBlock fileZoom,
        out TextBlock filePosition,
        out Border folderChip,
        out Border fileChip,
        out Border fileSeparator1,
        out Border fileSeparator2)
    {
        var row = new StackPanel
        {
            Classes = { "dual-pane-header", index == 0 ? "a" : "b" },
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Margin = new Thickness(16, 16, index == 1 ? 166 : 16, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        row.SetValue(Panel.ZIndexProperty, 30);
        row.ClipToBounds = true;

        badge = new Border { Classes = { "dual-badge", index == 0 ? "a" : "b" } };
        badge.Child = new TextBlock
        {
            Text = index == 0 ? "A" : "B",
            Classes = { "dual-badge-text" }
        };
        row.Children.Add(badge);

        folderChip = new Border
        {
            Classes = { "overlay-chip", "dual-folder-chip", index == 0 ? "a" : "b" },
            MaxWidth = 190,
            ClipToBounds = true
        };
        var folderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        folderRow.Children.Add(new IconPath
        {
            Data = FindGeometry("IconFolder"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            Width = 14,
            Height = 14
        });
        folderName = new TextBlock { MaxWidth = 125 };
        folderName.Classes.Add("chrome-name");
        folderRow.Children.Add(folderName);
        folderCount = new TextBlock();
        folderCount.Classes.Add("chrome-meta");
        folderRow.Children.Add(folderCount);
        folderChip.Child = folderRow;
        row.Children.Add(folderChip);

        fileChip = new Border
        {
            Classes = { "overlay-chip", "dual-file-chip", index == 0 ? "a" : "b" },
            MaxWidth = 430,
            Padding = new Thickness(9, 0),
            ClipToBounds = true
        };
        var fileRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,46"),
            VerticalAlignment = VerticalAlignment.Center
        };
        fileName = new TextBlock { MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
        fileName.Classes.Add("chrome-file");
        fileRow.Children.Add(fileName);
        fileSeparator1 = new Border
        {
            Width = 1,
            Height = 15,
            Margin = new Thickness(4, 0),
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF"))
        };
        Grid.SetColumn(fileSeparator1, 1);
        fileRow.Children.Add(fileSeparator1);
        fileMeta = new TextBlock { Margin = new Thickness(0, 0, 4, 0) };
        fileMeta.Classes.Add("chrome-meta");
        Grid.SetColumn(fileMeta, 2);
        fileRow.Children.Add(fileMeta);
        fileZoom = new TextBlock { Margin = new Thickness(0, 0, 6, 0) };
        fileZoom.Classes.Add("chrome-meta");
        Grid.SetColumn(fileZoom, 3);
        fileRow.Children.Add(fileZoom);
        fileSeparator2 = new Border
        {
            Width = 1,
            Height = 15,
            Margin = new Thickness(1, 0, 4, 0),
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF"))
        };
        Grid.SetColumn(fileSeparator2, 4);
        fileRow.Children.Add(fileSeparator2);
        filePosition = new TextBlock
        {
            Width = 46,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.Parse(index == 0 ? "#F4A593" : "#8BC8BB"))
        };
        filePosition.Classes.Add("chrome-meta");
        Grid.SetColumn(filePosition, 5);
        fileRow.Children.Add(filePosition);
        fileChip.Child = fileRow;
        row.Children.Add(fileChip);
        return row;
    }

    private Border BuildEmpty(int index, out TextBlock title, out TextBlock detail)
    {
        var card = new Border();
        card.Classes.Add("dual-empty-card");
        card.IsVisible = false;

        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = index == 0
                ? new SolidColorBrush(Color.Parse("#1FE3563B"))
                : new SolidColorBrush(Color.Parse("#1F2E6F63")),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.Child = new IconPath
        {
            Data = FindGeometry("IconFolderPlus"),
            Stroke = index == 0
                ? new SolidColorBrush(Color.Parse("#E3563B"))
                : new SolidColorBrush(Color.Parse("#2E6F63")),
            StrokeThickness = 1.6,
            Width = 22,
            Height = 22
        };
        stack.Children.Add(icon);

        title = new TextBlock
        {
            Text = _services.Localization.T("L.DualEmptyTitle"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(title);
        detail = new TextBlock
        {
            Text = _services.Localization.T("L.DualEmptyDetail"),
            Classes = { "dual-empty-detail" }
        };
        stack.Children.Add(detail);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var folderButton = new Button
        {
            Content = _services.Localization.T("L.OpenFolder").TrimEnd('…'),
            Classes = { "empty-primary" }
        };
        folderButton.Click += (_, _) => OpenRequested?.Invoke(this, new DualOpenRequest(index, true));
        var imageButton = new Button
        {
            Content = _services.Localization.T("L.OpenFile").TrimEnd('…'),
            Classes = { "empty-secondary" }
        };
        imageButton.Click += (_, _) => OpenRequested?.Invoke(this, new DualOpenRequest(index, false));
        actions.Children.Add(folderButton);
        actions.Children.Add(imageButton);
        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private Border BuildDivider()
    {
        var host = new Border
        {
            Width = 18,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.SizeWestEast)
        };
        var line = new Border();
        line.Classes.Add("dual-divider-line");
        var grip = new Border
        {
            Width = 18,
            Height = 44,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#C40A0E13")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "⋮",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.Parse("#B9B1A5")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        host.Child = new Grid { Children = { line, grip } };
        host.PointerPressed += OnDividerPointerPressed;
        host.PointerMoved += OnDividerPointerMoved;
        host.PointerReleased += OnDividerPointerReleased;
        host.DoubleTapped += (_, _) => ResetDivider();
        return host;
    }

    private Border BuildDock()
    {
        var dock = new Border { Classes = { "dock" }, HorizontalAlignment = HorizontalAlignment.Center };
        dock.SetValue(Panel.ZIndexProperty, 50);
        return dock;
    }

    private TextBlock BuildDockCore(
        Border dock,
        out Button infoButton,
        out Button filmstripButton,
        out TextBlock zoomText)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        _activeBadge = new Border { Classes = { "dual-dock-pane", "a" } };
        _activeBadgeText = new TextBlock { Text = "A", Classes = { "dual-dock-pane-text" } };
        _activeBadge.Child = _activeBadgeText;
        row.Children.Insert(0, _activeBadge);

        Button Tool(string icon, string tooltip, Action action)
        {
            var button = new Button
            {
                Content = new IconPath
                {
                    Data = FindGeometry(icon),
                    Width = 18,
                    Height = 18
                },
                Classes = { "tool" }
            };
            ToolTip.SetTip(button, tooltip);
            button.Click += (_, _) => action();
            return button;
        }

        _zoomOutButton = Tool("IconMinus", _services.Localization.T("L.ZoomOut"), () => Active.Viewer.ZoomOut());
        row.Children.Add(_zoomOutButton);
        zoomText = new TextBlock { Text = "100%" };
        zoomText.Classes.Add("dock-zoom");
        row.Children.Add(zoomText);
        _zoomInButton = Tool("IconPlus", _services.Localization.T("L.ZoomIn"), () => Active.Viewer.ZoomIn());
        row.Children.Add(_zoomInButton);
        row.Children.Add(new Border { Classes = { "dock-divider" } });
        _fitButton = Tool("IconFit", _services.Localization.T("L.FitWindow"),
            () => Active.Viewer.SetZoomMode(ViewerZoomMode.FitWindow));
        row.Children.Add(_fitButton);
        _actualButton = new Button
        {
            Content = new TextBlock { Text = "1:1", Classes = { "tool-text" } },
            Classes = { "tool", "wide" }
        };
        ToolTip.SetTip(_actualButton, _services.Localization.T("L.ActualSize"));
        _actualButton.Click += (_, _) => Active.Viewer.SetZoomMode(ViewerZoomMode.Actual);
        row.Children.Add(_actualButton);
        row.Children.Add(new Border { Classes = { "dock-divider" } });

        infoButton = Tool("IconInfo", _services.Localization.T("L.InfoPanel"), () => SetInfoVisible(!_infoVisible));
        row.Children.Add(infoButton);
        filmstripButton = Tool("IconFilmstrip", _services.Localization.T("L.Filmstrip"),
            () => SetFilmstripVisible(!_filmstripVisible));
        row.Children.Add(filmstripButton);
        row.Children.Add(Tool("IconSwap", _services.Localization.T("L.SwapPanes"), SwapPanes));
        row.Children.Add(new Border { Classes = { "dock-divider" } });
        row.Children.Add(Tool("IconClose", _services.Localization.T("L.CloseCompare"),
            () => ExitRequested?.Invoke(this, EventArgs.Empty)));
        dock.Child = row;
        return zoomText;
    }

    private Border BuildFilmstrip(out FilmHalf filmA, out FilmHalf filmB, out Button close)
    {
        var strip = new Border { Classes = { "dual-filmstrip" }, IsVisible = false };
        strip.SetValue(Panel.ZIndexProperty, 30);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
        filmA = BuildFilmHalf(0);
        filmB = BuildFilmHalf(1);
        Grid.SetColumn(filmA.Host, 0);
        Grid.SetColumn(filmB.Host, 2);

        var split = new Border { Classes = { "dual-film-split" } };
        Grid.SetColumn(split, 1);

        close = new Button
        {
            Content = new TextBlock { Text = "×", FontSize = 16 },
            Classes = { "dual-film-close" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 11, 0)
        };
        close.SetValue(Panel.ZIndexProperty, 4);
        close.Click += (_, _) => SetFilmstripVisible(false);
        Grid.SetColumnSpan(close, 3);

        grid.Children.Add(filmA.Host);
        grid.Children.Add(split);
        grid.Children.Add(filmB.Host);
        grid.Children.Add(close);
        strip.Child = grid;
        return strip;
    }

    private FilmHalf BuildFilmHalf(int index)
    {
        var host = new Border { Classes = { "dual-film-half", index == 0 ? "a" : "b" } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };

        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Border { Classes = { "dual-film-badge", index == 0 ? "a" : "b" } };
        var badgeText = new TextBlock
        {
            Text = index == 0 ? "A" : "B",
            Classes = { "dual-badge-text" }
        };
        badge.Child = badgeText;
        head.Children.Add(badge);

        var folder = new TextBlock
        {
            Classes = { "dual-film-title" },
            MaxWidth = 190,
            VerticalAlignment = VerticalAlignment.Center
        };
        head.Children.Add(folder);
        var count = new TextBlock { Classes = { "dual-film-meta" }, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(count);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 7, 0, 0)
        };
        var thumbs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        scroll.Content = thumbs;

        Grid.SetRow(scroll, 1);
        Grid.SetRow(head, 0);
        grid.Children.Add(head);
        grid.Children.Add(scroll);
        host.Child = grid;

        return new FilmHalf(index, host, badge, badgeText, folder, count, scroll, thumbs);
    }

    private Border BuildInfo(out StackPanel content, out Button tabA, out Button tabB)
    {
        var info = new Border { Classes = { "info-overlay" }, IsVisible = false };
        info.SetValue(Panel.ZIndexProperty, 45);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 14, 10, 4) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        tabA = new Button { Content = "A", Classes = { "dual-tab", "a", "active" } };
        tabB = new Button { Content = "B", Classes = { "dual-tab", "b" } };
        tabA.Click += (_, _) => SetActive(0);
        tabB.Click += (_, _) => SetActive(1);
        tabs.Children.Add(tabA);
        tabs.Children.Add(tabB);
        header.Children.Add(tabs);
        var title = new TextBlock
        {
            Text = _services.Localization.T("L.InfoPanel"),
            Classes = { "info-title" },
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var close = new Button
        {
            Content = new TextBlock { Text = "×", FontSize = 16 },
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#9A9286")),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => SetInfoVisible(false);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        grid.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        content = new StackPanel { Spacing = 0, Margin = new Thickness(16, 4, 16, 14) };
        scroll.Content = content;
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        info.Child = grid;
        return info;
    }

    private Border BuildMenuScrim()
    {
        var scrim = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#01000000")),
            IsVisible = false
        };
        scrim.SetValue(Panel.ZIndexProperty, 54);
        scrim.PointerPressed += (_, args) =>
        {
            ClosePaneMenu();
            args.Handled = true;
        };
        return scrim;
    }

    private Border BuildPaneMenu(
        out Border badge,
        out TextBlock badgeText,
        out TextBlock folder,
        out TextBlock meta,
        out Button openFolder,
        out Button openImage,
        out Button reveal,
        out Button copyImage,
        out Button copyPath,
        out Button swap,
        out Button exit)
    {
        var menu = new Border { Classes = { "dual-menu" }, IsVisible = false };
        menu.SetValue(Panel.ZIndexProperty, 55);
        var stack = new StackPanel { Spacing = 1 };

        var head = new Border { Classes = { "dual-menu-head" } };
        var headGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        badge = new Border { Classes = { "dual-menu-badge", "b" } };
        badgeText = new TextBlock { Text = "B", Classes = { "dual-badge-text" } };
        badge.Child = badgeText;
        var headText = new StackPanel { Spacing = 2, Margin = new Thickness(9, 0, 0, 0) };
        folder = new TextBlock { Classes = { "dual-menu-folder" } };
        meta = new TextBlock { Classes = { "dual-menu-meta" } };
        headText.Children.Add(folder);
        headText.Children.Add(meta);
        Grid.SetColumn(headText, 1);
        headGrid.Children.Add(badge);
        headGrid.Children.Add(headText);
        head.Child = headGrid;
        stack.Children.Add(head);

        openFolder = BuildMenuItem("IconFolderPlus", _services.Localization.T("L.OpenFolder"),
            "Ctrl+Shift+O", () => ActivateMenuFolder(), primary: true);
        openImage = BuildMenuItem("IconImage", _services.Localization.T("L.OpenFile"),
            "Ctrl+O", () => ActivateMenuImage());
        reveal = BuildMenuItem("IconFolder", _services.Localization.T("L.ShowInFolder"), null,
            ActivateMenuReveal);
        copyImage = BuildMenuItem("IconImage", _services.Localization.T("L.CopyImage"), "Ctrl+C",
            () => _ = CopyMenuImageAsync());
        copyPath = BuildMenuItem("IconLink", _services.Localization.T("L.CopyPath"), null,
            () => _ = CopyMenuPathAsync());
        swap = BuildMenuItem("IconSwap", _services.Localization.T("L.SwapPanes"), "X",
            () => { ClosePaneMenu(); SwapPanes(); });
        exit = BuildMenuItem("IconClose", _services.Localization.T("L.CloseCompare"), "D",
            () => { ClosePaneMenu(); ExitRequested?.Invoke(this, EventArgs.Empty); });

        stack.Children.Add(openFolder);
        stack.Children.Add(openImage);
        stack.Children.Add(new Border { Classes = { "dual-menu-sep" } });
        stack.Children.Add(reveal);
        stack.Children.Add(copyImage);
        stack.Children.Add(copyPath);
        stack.Children.Add(new Border { Classes = { "dual-menu-sep" } });
        stack.Children.Add(swap);
        stack.Children.Add(exit);
        stack.Children.Add(new TextBlock
        {
            Text = _services.Localization.T("L.ContextMenuNote"),
            Classes = { "dual-menu-note" }
        });
        menu.Child = stack;
        return menu;
    }

    private Button BuildMenuItem(string iconKey, string label, string? shortcut, Action action, bool primary = false)
    {
        var button = new Button { Classes = { "dual-menu-item" } };
        _menuItems.Add(button);
        button.PointerEntered += (_, _) => button.Classes.Set("hover", true);
        button.PointerMoved += (_, _) => button.Classes.Set("hover", true);
        button.PointerExited += (_, _) => button.Classes.Set("hover", false);
        if (primary)
        {
            button.Classes.Add("primary");
        }

        var grid = new Grid
        {
            Width = 242,
            HorizontalAlignment = HorizontalAlignment.Left,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new IconPath
        {
            Data = FindGeometry(iconKey),
            Stroke = primary ? Brushes.White : new SolidColorBrush(Color.Parse("#D8D1C5")),
            StrokeThickness = 1.6,
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon);
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = primary ? Brushes.White : new SolidColorBrush(Color.Parse("#F0EBE2"))
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (shortcut is not null)
        {
            var hint = new TextBlock
            {
                Text = shortcut,
                Classes = { "dual-menu-shortcut" },
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.Parse(primary ? "#B7E6DC" : "#ABA49A"))
            };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        button.Content = grid;
        button.Click += (_, _) => action();
        return button;
    }

    private void OpenPaneMenu(int paneIndex, Point point)
    {
        SetActive(paneIndex);
        ShowChrome();
        _menuPane = paneIndex;
        var pane = Pane(paneIndex);
        _menuBadge.Classes.Set("a", paneIndex == 0);
        _menuBadge.Classes.Set("b", paneIndex == 1);
        _menuBadgeText.Text = paneIndex == 0 ? "A" : "B";
        _menuFolder.Text = pane.Folder is null ? _services.Localization.T("L.NoFolder") : Path.GetFileName(pane.Folder);
        _menuMeta.Text = pane.Files.Count > 0
            ? $"{pane.FileIndex + 1:D2} / {pane.Files.Count:D2}"
            : string.Empty;
        var hasImage = pane.CurrentPath is not null;
        _menuReveal.IsEnabled = hasImage;
        _menuCopyImage.IsEnabled = hasImage;
        _menuCopyPath.IsEnabled = hasImage;
        _menuScrim.IsVisible = true;
        _paneMenu.IsVisible = true;
        _menuVisible = true;
        foreach (var item in _menuItems)
        {
            item.Classes.Remove("hover");
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        var x = Math.Clamp(point.X + 8, 8, Math.Max(8, width - 286));
        var y = Math.Clamp(point.Y + 8, 8, Math.Max(8, height - 360));
        _paneMenu.Margin = new Thickness(x, y, 0, 0);
    }

    private void ClosePaneMenu()
    {
        _menuVisible = false;
        foreach (var item in _menuItems)
        {
            item.Classes.Remove("hover");
        }

        _menuScrim.IsVisible = false;
        _paneMenu.IsVisible = false;
    }

    private void ActivateMenuFolder()
    {
        var pane = _menuPane;
        ClosePaneMenu();
        OpenRequested?.Invoke(this, new DualOpenRequest(pane, true));
    }

    private void ActivateMenuImage()
    {
        var pane = _menuPane;
        ClosePaneMenu();
        OpenRequested?.Invoke(this, new DualOpenRequest(pane, false));
    }

    private void ActivateMenuReveal()
    {
        if (Pane(_menuPane).CurrentPath is not { } path)
        {
            return;
        }

        ClosePaneMenu();
        RevealRequested?.Invoke(this, new DualRevealRequest(path));
    }

    private async Task CopyMenuImageAsync()
    {
        var bitmap = Pane(_menuPane).Bitmap ?? Pane(_menuPane).Animation?.Frames.FirstOrDefault();
        ClosePaneMenu();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (bitmap is null || clipboard is null)
        {
            return;
        }

        using var transfer = new DataTransfer();
        var item = new DataTransferItem();
        item.SetBitmap(bitmap);
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    private async Task CopyMenuPathAsync()
    {
        var path = Pane(_menuPane).CurrentPath;
        ClosePaneMenu();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (path is null || clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(path);
    }

    private Border BuildPaneNav(int index, out Border badge, out Button previous, out Button next)
    {
        var group = new Border { Classes = { "dual-pane-nav", index == 0 ? "a" : "b" }, IsVisible = false };
        group.SetValue(Panel.ZIndexProperty, 38);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        badge = new Border { Classes = { "dual-pane-nav-badge", index == 0 ? "a" : "b" } };
        badge.Child = new TextBlock
        {
            Text = index == 0 ? "A" : "B",
            Classes = { "dual-badge-text" }
        };
        row.Children.Add(badge);

        previous = CreatePaneNavButton(index, "IconChevronLeft", -1, "L.PreviousImage");
        next = CreatePaneNavButton(index, "IconChevronRight", 1, "L.NextImage");
        row.Children.Add(previous);
        row.Children.Add(next);
        group.Child = row;
        return group;
    }

    private Button CreatePaneNavButton(int index, string icon, int direction, string tooltipKey)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(icon),
                Stroke = new SolidColorBrush(Color.Parse("#F7F3EA")),
                StrokeThickness = 1.7,
                Width = 16,
                Height = 16
            },
            Classes = { "dual-pane-nav-button", index == 0 ? "a" : "b", direction < 0 ? "prev" : "next" }
        };
        ToolTip.SetTip(button,
            $"{(index == 0 ? "A" : "B")} · {_services.Localization.T(tooltipKey)}");
        button.Click += (_, _) => _ = NavigatePaneAsync(Pane(index), direction);
        return button;
    }

    private async Task NavigatePaneAsync(PaneSlot pane, int direction)
    {
        if (pane.Files.Count < 2)
        {
            return;
        }

        SetActive(pane.PaneNumber);
        var index = (pane.FileIndex + direction + pane.Files.Count) % pane.Files.Count;
        pane.FileIndex = index;
        pane.CurrentPath = pane.Files[index];
        await ApplySourceAsync(pane, new ImageSource(pane.Folder ?? string.Empty, pane.Files, index), index);
    }


    private async Task ApplySourceAsync(PaneSlot pane, ImageSource source, int index)
    {
        pane.LoadCts?.Cancel();
        pane.LoadCts?.Dispose();
        pane.LoadCts = new CancellationTokenSource();
        var token = pane.LoadCts.Token;
        var version = ++pane.LoadVersion;
        pane.Files = source.Files.ToList();
        pane.Folder = source.FolderPath;
        if (pane.Files.Count == 0)
        {
            pane.FileIndex = 0;
            pane.CurrentPath = null;
            ShowEmpty(pane);
            return;
        }

        index = Math.Clamp(index, 0, pane.Files.Count - 1);
        pane.FileIndex = index;
        var path = pane.Files[index];
        pane.CurrentPath = path;
        pane.Loading.IsVisible = true;
        pane.Empty.IsVisible = false;

        try
        {
            var decoded = await Task.Run(() =>
            {
                var metadata = ImageDecoder.ReadMetadata(path);
                var animation = metadata.IsAnimated
                    ? ImageDecoder.DecodeAnimation(path, 2400, 1600, token)
                    : null;
                var still = animation is null ? ImageDecoder.Decode(path, 2400, 1600, token) : null;
                return (metadata, animation, still);
            }, token);
            if (token.IsCancellationRequested || version != pane.LoadVersion)
            {
                return;
            }

            var oldBitmap = pane.Bitmap;
            var oldAnimation = pane.Animation;
            pane.Metadata = decoded.metadata;
            pane.Error = null;
            if (decoded.animation is not null)
            {
                var frames = new List<WriteableBitmap>(decoded.animation.Frames.Count);
                foreach (var frame in decoded.animation.Frames)
                {
                    frames.Add(BitmapFactory.FromDecoded(frame));
                }

                var first = decoded.animation.Frames[0];
                var animation = new AnimatedImage(
                    frames,
                    decoded.animation.DelaysMilliseconds,
                    first.SourceWidth,
                    first.SourceHeight,
                    first.Scale,
                    decoded.animation.RepetitionCount);
                pane.Animation = animation;
                pane.Bitmap = null;
                pane.Viewer.SetAnimation(animation, resetView: true);
            }
            else
            {
                var bitmap = BitmapFactory.FromDecoded(decoded.still!);
                pane.Bitmap = bitmap;
                pane.Animation = null;
                pane.Viewer.SetImage(bitmap, decoded.metadata.DisplayWidth, decoded.metadata.DisplayHeight,
                    bitmap.PixelSize.Width / (double)decoded.metadata.DisplayWidth, resetView: true,
                    animateImage: false);
            }

            oldBitmap?.Dispose();
            oldAnimation?.Dispose();
            pane.Loading.IsVisible = false;
            UpdatePaneLabels(pane);
            if (pane.PaneNumber == _activeIndex)
            {
                UpdateInfo();
                UpdateDock();
            }

            if (_filmstripVisible)
            {
                _ = RebuildFilmstripAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
        catch (Exception exception)
        {
            if (version == pane.LoadVersion)
            {
                ShowError(pane, exception.Message);
            }
        }
        finally
        {
            if (version == pane.LoadVersion)
            {
                pane.Loading.IsVisible = false;
            }
        }
    }

    private async Task NavigateAsync(int direction)
    {
        await NavigatePaneAsync(Active, direction);
    }

    private void SetActive(int index)
    {
        if (index is < 0 or > 1)
        {
            return;
        }

        _activeIndex = index;
        _a.ActiveFrame.IsVisible = index == 0;
        _b.ActiveFrame.IsVisible = index == 1;
        _a.Badge.Classes.Set("active", index == 0);
        _b.Badge.Classes.Set("active", index == 1);
        _activeBadge.Classes.Set("a", index == 0);
        _activeBadge.Classes.Set("b", index == 1);
        _activeBadge.Classes.Set("active", true);
        _activeBadgeText.Text = index == 0 ? "A" : "B";
        _infoTabA.Classes.Set("active", index == 0);
        _infoTabB.Classes.Set("active", index == 1);
        UpdateFilmstripActive();
        ApplyChrome();
        UpdateDock();
        UpdateInfo();
    }

    private void UpdateDock()
    {
        if (_zoomText.Parent is null)
        {
            return;
        }

        var hasImage = Active.CurrentPath is not null;
        _zoomOutButton.IsEnabled = hasImage;
        _zoomInButton.IsEnabled = hasImage;
        _fitButton.IsEnabled = hasImage;
        _actualButton.IsEnabled = hasImage;
        _zoomOutButton.Opacity = hasImage ? 1 : 0.32;
        _zoomInButton.Opacity = hasImage ? 1 : 0.32;
        _fitButton.Opacity = hasImage ? 1 : 0.32;
        _actualButton.Opacity = hasImage ? 1 : 0.32;
        _zoomText.Opacity = hasImage ? 1 : 0.32;
        _zoomText.Text = hasImage ? $"{Active.Viewer.ZoomPercent:0}%" : "—";
        _infoButton.Classes.Set("active", _infoVisible);
        var canShowFilmstrip = HasAnyImage;
        _filmstripButton.IsEnabled = canShowFilmstrip;
        _filmstripButton.Opacity = canShowFilmstrip ? 1 : 0.32;
        _filmstripButton.Classes.Set("active", _filmstripVisible);
        _dock.Margin = _filmstripVisible ? new Thickness(0, 0, 0, 166) : new Thickness(0, 0, 0, 22);
    }

    private void SwapPanes()
    {
        var a = _a;
        var b = _b;
        (a.Bitmap, b.Bitmap) = (b.Bitmap, a.Bitmap);
        (a.Animation, b.Animation) = (b.Animation, a.Animation);
        (a.Metadata, b.Metadata) = (b.Metadata, a.Metadata);
        (a.CurrentPath, b.CurrentPath) = (b.CurrentPath, a.CurrentPath);
        (a.Files, b.Files) = (b.Files, a.Files);
        (a.Folder, b.Folder) = (b.Folder, a.Folder);
        (a.FileIndex, b.FileIndex) = (b.FileIndex, a.FileIndex);
        RefreshViewer(a);
        RefreshViewer(b);
        UpdatePaneLabels(a);
        UpdatePaneLabels(b);
        SetActive(_activeIndex);
        if (_filmstripVisible)
        {
            _ = RebuildFilmstripAsync();
        }
    }

    private void RefreshViewer(PaneSlot pane)
    {
        if (pane.Animation is { } animation)
        {
            pane.Viewer.SetAnimation(animation, resetView: true);
            pane.Empty.IsVisible = false;
        }
        else if (pane.Bitmap is { } bitmap && pane.Metadata is { } metadata)
        {
            pane.Viewer.SetImage(bitmap, metadata.DisplayWidth, metadata.DisplayHeight,
                bitmap.PixelSize.Width / (double)metadata.DisplayWidth, resetView: true, animateImage: false);
            pane.Empty.IsVisible = false;
        }
        else
        {
            pane.Viewer.ClearImage();
            ShowEmpty(pane);
        }
    }

    private void ShowError(PaneSlot pane, string message)
    {
        pane.Error = message;
        pane.Viewer.ClearImage();
        pane.Bitmap?.Dispose();
        pane.Bitmap = null;
        pane.Animation?.Dispose();
        pane.Animation = null;
        pane.Loading.IsVisible = false;
        pane.EmptyTitle.Text = _services.Localization.T("L.LoadingFailed");
        pane.EmptyDetail.Text = message;
        pane.Empty.IsVisible = true;
    }

    private void ShowEmpty(PaneSlot pane)
    {
        pane.Viewer.ClearImage();
        pane.Bitmap?.Dispose();
        pane.Bitmap = null;
        pane.Animation?.Dispose();
        pane.Animation = null;
        pane.Metadata = null;
        pane.CurrentPath = null;
        pane.Error = null;
        pane.Loading.IsVisible = false;
        pane.EmptyTitle.Text = _services.Localization.T("L.DualEmptyTitle");
        pane.EmptyDetail.Text = _services.Localization.T("L.DualEmptyDetail");
        pane.Empty.IsVisible = true;
        UpdatePaneLabels(pane);
    }

    private void UpdatePaneLabels(PaneSlot pane)
    {
        pane.FolderCount.Text = pane.Files.Count > 0
            ? pane.Files.Count.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        pane.FileMeta.Text = pane.Metadata is null
            ? string.Empty
            : $"{pane.Metadata.DisplayWidth} × {pane.Metadata.DisplayHeight}";
        pane.FileZoom.Text = pane.Metadata is null
            ? string.Empty
            : $"· {pane.Viewer.ZoomPercent:0}%";
        pane.FileChip.IsVisible = pane.CurrentPath is not null;
        pane.FilePosition.Text = pane.CurrentPath is not null && pane.Files.Count > 0
            ? $"{pane.FileIndex + 1:D2} / {pane.Files.Count:D2}"
            : string.Empty;
        ApplyPaneHeaderLayout(pane);
        ApplyChrome();
    }

    private void SetInfoVisible(bool visible)
    {
        ShowChrome();
        _infoVisible = visible;
        if (visible)
        {
            SetFilmstripVisible(false);
        }

        _info.IsVisible = visible;
        UpdateDock();
        if (visible)
        {
            UpdateInfo();
        }
    }

    private void UpdateInfo()
    {
        _infoContent.Children.Clear();
        var pane = Active;
        if (pane.Metadata is not { } metadata)
        {
            _infoContent.Children.Add(new TextBlock
            {
                Text = _services.Localization.T("L.NoImage"),
                Foreground = new SolidColorBrush(Color.Parse("#69645C")),
                Margin = new Thickness(0, 12, 0, 0)
            });
            return;
        }

        AddInfoTitle(_infoContent, metadata.FileName);
        AddInfoSection(_infoContent, _services.Localization.T("L.SectionFile"),
            (_services.Localization.T("L.InfoPath"), metadata.FilePath),
            (_services.Localization.T("L.InfoSize"), FormatBytes(metadata.FileSizeBytes)));
        AddInfoSection(_infoContent, _services.Localization.T("L.SectionImage"),
            (_services.Localization.T("L.InfoDimensions"), $"{metadata.DisplayWidth} × {metadata.DisplayHeight}"),
            (_services.Localization.T("L.InfoFormat"), metadata.FormatName),
            (_services.Localization.T("L.InfoBitDepth"), metadata.BitDepth.ToString(CultureInfo.InvariantCulture)));
        AddInfoSection(_infoContent, _services.Localization.T("L.SectionColor"),
            (_services.Localization.T("L.InfoColorSpace"), metadata.ColorSpaceDisplay));
    }

    private static void AddInfoTitle(StackPanel content, string text)
    {
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 8)
        });
    }

    private static void AddInfoSection(StackPanel content, string title, params (string Label, string Value)[] rows)
    {
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#9A9286")),
            Margin = new Thickness(0, 10, 0, 4)
        });
        foreach (var (label, value) in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("82,*"), Margin = new Thickness(0, 2) };
            grid.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.Parse("#69645C"))
            });
            var valueText = new TextBlock
            {
                Text = value,
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);
            content.Children.Add(grid);
        }
    }

    private void SetFilmstripVisible(bool visible)
    {
        if (visible && !HasAnyImage)
        {
            return;
        }

        ShowChrome();
        _filmstripVisible = visible;
        if (visible)
        {
            SetInfoVisible(false);
            _ = RebuildFilmstripAsync();
        }
        else
        {
            _filmstripCts?.Cancel();
        }

        _filmstrip.IsVisible = visible;
        UpdateDock();
    }

    private async Task RebuildFilmstripAsync()
    {
        _filmstripCts?.Cancel();
        _filmstripCts?.Dispose();
        _filmstripCts = new CancellationTokenSource();
        var token = _filmstripCts.Token;
        var version = ++_filmstripVersion;

        _filmA.Thumbs.Children.Clear();
        _filmB.Thumbs.Children.Clear();
        foreach (var bitmap in _thumbnailBitmaps)
        {
            bitmap.Dispose();
        }

        _thumbnailBitmaps.Clear();
        if (!_filmstripVisible || _disposed)
        {
            return;
        }

        await PopulateFilmHalfAsync(_filmA, _a, token, version);
        await PopulateFilmHalfAsync(_filmB, _b, token, version);
        UpdateFilmstripActive();
    }

    private async Task PopulateFilmHalfAsync(FilmHalf half, PaneSlot pane, CancellationToken token, int version)
    {
        var empty = pane.CurrentPath is null;
        half.FolderName.Text = empty || pane.Folder is null ? string.Empty : Path.GetFileName(pane.Folder);
        half.Count.Text = empty
            ? string.Empty
            : string.Format(_services.Localization.T("L.ImageCount"), pane.Files.Count);
        half.Scroll.IsVisible = !empty;
        half.Host.IsHitTestVisible = !empty;
        if (empty)
        {
            return;
        }

        var files = pane.Files.Take(24).ToList();
        for (var index = 0; index < files.Count; index++)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var itemIndex = index;
            var image = new Image { Stretch = Stretch.UniformToFill };
            var accent = new SolidColorBrush(Color.Parse(pane.PaneNumber == 0 ? "#E3563B" : "#2E6F63"));
            var thumb = new Border
            {
                Width = 88,
                Height = 54,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#20262E")),
                ClipToBounds = true,
                BorderThickness = new Thickness(2),
                BorderBrush = itemIndex == pane.FileIndex ? accent : Brushes.Transparent,
                Child = image
            };
            var frame = new Grid { Width = 88, Height = 54 };
            frame.Children.Add(thumb);
            if (itemIndex == pane.FileIndex)
            {
                frame.Children.Add(CreateThumbBadge(pane.PaneNumber == 0 ? "A" : "B", pane.PaneNumber == 0));
            }

            var label = new TextBlock
            {
                Text = Path.GetFileName(files[itemIndex]),
                FontSize = 9,
                MaxWidth = 88,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.Parse("#BEB6A9"))
            };
            var button = new Button
            {
                Content = new StackPanel { Spacing = 3, Children = { frame, label } },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 92
            };
            button.Click += (_, _) =>
            {
                SetActive(pane.PaneNumber);
                pane.FileIndex = itemIndex;
                pane.CurrentPath = pane.Files[itemIndex];
                _ = ApplySourceAsync(pane, new ImageSource(pane.Folder ?? string.Empty, pane.Files, itemIndex), itemIndex);
            };
            half.Thumbs.Children.Add(button);

            try
            {
                var path = files[itemIndex];
                var decoded = await Task.Run(() => ImageDecoder.Decode(path, 160, 160, token), token);
                if (token.IsCancellationRequested || version != _filmstripVersion)
                {
                    return;
                }

                var bitmap = BitmapFactory.FromDecoded(decoded);
                _thumbnailBitmaps.Add(bitmap);
                image.Source = bitmap;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A broken thumbnail must not remove the filmstrip item.
            }
        }
    }

    private void UpdateFilmstripActive()
    {
        _filmA.Host.Classes.Set("active", _activeIndex == 0);
        _filmB.Host.Classes.Set("active", _activeIndex == 1);
        _filmA.Badge.Classes.Set("active", _activeIndex == 0);
        _filmB.Badge.Classes.Set("active", _activeIndex == 1);
    }

    private static Border CreateThumbBadge(string letter, bool isA) =>
        new()
        {
            Width = 17,
            Height = 15,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse(isA ? "#E3563B" : "#2E6F63")),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0),
            Child = new TextBlock
            {
                Text = letter,
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
                FontSize = 8,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

    private void ApplyPaneHeaderLayout(PaneSlot pane)
    {
        var paneWidth = pane.Host.Bounds.Width;
        if (paneWidth <= 0)
        {
            return;
        }

        var windowWidth = Bounds.Width;
        var compact = windowWidth > 0 && windowWidth < 1120;
        var narrow = windowWidth >= 1120 && windowWidth < 1240;
        var wide = windowWidth >= 1600;
        var rightReserve = pane.PaneNumber == 1 ? 166 : 16;
        var available = Math.Max(160, paneWidth - 16 - rightReserve);
        pane.Header.MaxWidth = available;

        var folderMax = compact ? 120 : available >= 500 ? 190 : 150;
        var fileMax = Math.Min(340, Math.Max(120, available - 44 - folderMax));
        if (compact)
        {
            fileMax = Math.Min(fileMax, 170);
        }

        pane.FolderChip.MaxWidth = folderMax;
        pane.FileChip.MaxWidth = fileMax;
        pane.FileChip.Width = fileMax;

        var folderChars = compact ? 8 : 12;
        var fileChars = wide ? 14 : narrow ? 10 : 8;
        pane.FolderName.MaxWidth = compact ? 60 : 82;
        pane.FileName.MaxWidth = fileChars >= 14 ? 96 : fileChars >= 10 ? 70 : 52;

        var folderText = pane.Folder is null ? string.Empty : Path.GetFileName(pane.Folder);
        var fileText = pane.CurrentPath is null ? string.Empty : Path.GetFileName(pane.CurrentPath);
        pane.FolderName.Text = TruncateLabel(folderText, folderChars);
        pane.FileName.Text = TruncateFileName(fileText, fileChars);
        ToolTip.SetTip(pane.FolderChip, string.IsNullOrEmpty(folderText) ? null : folderText);
        ToolTip.SetTip(pane.FileChip, string.IsNullOrEmpty(fileText) ? null : fileText);

        var showDimensions = !compact && !narrow;
        var showZoom = !compact;
        pane.FileMeta.IsVisible = showDimensions;
        pane.FileSeparator1.IsVisible = showDimensions;
        pane.FileZoom.IsVisible = showZoom;
        pane.FileSeparator2.IsVisible = true;
        pane.FilePosition.IsVisible = true;
        pane.FolderCount.IsVisible = !compact;
    }

    private static string TruncateLabel(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, Math.Max(1, maxChars - 1)), "…");
    }

    private static string TruncateFileName(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        var extension = Path.GetExtension(value).TrimStart('.');
        if (extension.Length > 0 && extension.Length + 2 <= maxChars)
        {
            var prefixLength = maxChars - extension.Length - 1;
            var prefix = value[..prefixLength].TrimEnd('.');
            if (prefix.Length > 0)
            {
                return string.Concat(prefix, "…", extension);
            }
        }

        return string.Concat(value.AsSpan(0, Math.Max(1, maxChars - 1)), "…");
    }

    private void ApplyCompactLayout()
    {
        var compact = Bounds.Width > 0 && Bounds.Width < 1120;
        ApplyToPane(_a);
        ApplyToPane(_b);

        ApplyPaneHeaderLayout(_a);
        ApplyPaneHeaderLayout(_b);

        void ApplyToPane(PaneSlot pane)
        {
            pane.NavGroup.Height = compact ? 34 : 38;
            pane.NavGroup.Padding = new Thickness(compact ? 2 : 3);
            pane.NavGroup.Margin = new Thickness(0, compact ? 58 : 64, 0, 0);
            pane.NavBadge.Width = compact ? 28 : 30;
            pane.NavBadge.Height = compact ? 28 : 30;
            pane.NavPrev.Width = compact ? 28 : 30;
            pane.NavPrev.Height = compact ? 28 : 30;
            pane.NavNext.Width = compact ? 28 : 30;
            pane.NavNext.Height = compact ? 28 : 30;
        }
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

    private void UpdatePaneNavState(PaneSlot pane)
    {
        var canNavigate = pane.CurrentPath is not null && pane.Files.Count > 1;
        pane.NavGroup.IsVisible = canNavigate;
        if (!canNavigate)
        {
            pane.NavGroup.Opacity = 0;
            pane.NavGroup.IsHitTestVisible = false;
            pane.NavBadge.Classes.Set("active", false);
            return;
        }

        var active = pane.PaneNumber == _activeIndex;
        pane.NavGroup.Opacity = _chromeVisible ? (active ? 1 : 0.6) : 0;
        pane.NavGroup.IsHitTestVisible = _chromeVisible;
        pane.NavBadge.Classes.Set("active", _chromeVisible && active);
    }

    private void OnPaneZoomChanged(object? sender, ZoomChangedEventArgs e)
    {
        if (sender == _a.Viewer)
        {
            UpdatePaneLabels(_a);
        }
        else if (sender == _b.Viewer)
        {
            UpdatePaneLabels(_b);
        }

        if (sender == Active.Viewer)
        {
            UpdateDock();
        }
    }

    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (sender == _a.Viewer)
        {
            UpdatePaneLabels(_a);
        }
        else if (sender == _b.Viewer)
        {
            UpdatePaneLabels(_b);
        }
    }

    private void OnDividerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggingDivider = true;
        ShowChrome();
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnDividerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingDivider)
        {
            return;
        }

        var width = _panesGrid.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(e.GetPosition(_panesGrid).X / width, 0.25, 0.75);
        _panesGrid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
        _panesGrid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
        e.Handled = true;
    }

    private void OnDividerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingDivider = false;
        e.Pointer.Capture(null);
        ShowChrome();
        e.Handled = true;
    }

    private void ResetDivider()
    {
        _panesGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        _panesGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var path = GetFirstDroppedPath(e);
        var supported = path is not null && (Directory.Exists(path) || SupportedFormats.IsSupported(path));
        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        if (supported && _panesGrid.Bounds.Width > 0)
        {
            var paneIndex = e.GetPosition(_panesGrid).X < _panesGrid.Bounds.Width / 2 ? 0 : 1;
            SetDragPane(paneIndex);
        }

        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        SetDragPane(-1);
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = GetFirstDroppedPath(e);
        if (path is not null)
        {
            var pane = e.GetPosition(_panesGrid).X < _panesGrid.Bounds.Width / 2 ? 0 : 1;
            SetDragPane(-1);
            await OpenPathAsync(pane, path);
        }

        e.Handled = true;
    }

    private void SetDragPane(int paneIndex)
    {
        if (_dragPane == paneIndex)
        {
            return;
        }

        if (_dragPane >= 0)
        {
            var previous = Pane(_dragPane);
            previous.Host.BorderThickness = new Thickness(0);
            previous.Host.BorderBrush = null;
        }

        _dragPane = paneIndex;
        if (paneIndex >= 0)
        {
            var pane = Pane(paneIndex);
            pane.Host.BorderThickness = new Thickness(2);
            pane.Host.BorderBrush = new SolidColorBrush(
                Color.Parse(paneIndex == 0 ? "#E3563B" : "#2E6F63"));
        }
    }

    private static string? GetFirstDroppedPath(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    private PaneSlot Active => Pane(_activeIndex);

    private PaneSlot Pane(int index) => index == 0 ? _a : _b;

    private Geometry FindGeometry(string key) =>
        (this.FindResource(key) as Geometry ?? Application.Current?.FindResource(key) as Geometry)
        ?? StreamGeometry.Parse("M0 0");

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };

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
