using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.Core.Imaging;
using AirGlance.Core.Files;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AirGlance.App.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;

    private readonly CommandLineOptions _options;

    private readonly BenchmarkRecorder? _benchmark;

    private readonly MainWindowViewModel _viewModel;

    private readonly DispatcherTimer _cursorTimer;

    private readonly DispatcherTimer _chromeTimer;

    private readonly DispatcherTimer? _benchmarkTimeout;

    private readonly ImageViewer Viewer = new();

    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    private FilmstripView? _filmstripView;

    private InfoPanelView? _infoPanel;

    private AnalysisPanelView? _analysisPanel;

    private ContextMenu? _contextMenu;

    private MenuItem? _analysisMenu;

    private MenuItem? _eyedropperMenuItem;

    private bool _panelsCreated;

    private bool _isFullscreen;

    private bool _initialZoomApplied;

    private bool _cursorHidden;

    private bool _chromeVisible = true;

    private bool _dualMode;

    private bool _cropMode;

    private bool _cropDragging;

    private CropRegion _cropBeforeEnter = CropRegion.Full;

    private string _cropAspectBeforeEnter = "free";

    private string _cropAspect = "free";

    private DualCanvasView? _dualView;

    private Border? _exportScrim;

    private Grid _root = null!;

    private Border TopBar = null!;

    private Border FolderChip = null!;

    private Border BrandChip = null!;

    private Border FileChip = null!;

    private Border WindowChip = null!;

    private Border CanvasBorder = null!;

    private Border Dock = null!;

    private Grid PrevHost = null!;

    private Grid NextHost = null!;

    private Border InfoHost = null!;

    private ContentControl InfoContentHost = null!;

    private Border AnalysisHost = null!;

    private ContentControl AnalysisContentHost = null!;

    private Border FilmstripHost = null!;

    private EditPanelView EditPanel = null!;

    private CropOverlay CropControl = null!;

    private CropToolbarView CropBar = null!;

    private Border CropModeTag = null!;

    private Border CropSafeTag = null!;

    private Border EmptyCard = null!;

    private Border LoadingChip = null!;

    private Border TileLoadingChip = null!;

    private Border DragOverlay = null!;

    private Border ToastBorder = null!;

    private Border FullscreenHint = null!;

    private StackPanel CaptionButtons = null!;

    private TextBlock FileNameText = null!;

    private TextBlock FolderNameText = null!;

    private TextBlock FolderCountText = null!;

    private TextBlock FileMetaText = null!;

    private TextBlock FilePositionText = null!;

    private TextBlock OverlayTitle = null!;

    private TextBlock OverlayMessage = null!;

    private TextBlock OverlayHint = null!;

    private TextBlock LoadingText = null!;

    private TextBlock TileLoadingText = null!;

    private TextBlock DropHintText = null!;

    private TextBlock TransientMessageText = null!;

    private TextBlock DockZoomText = null!;

    private TextBlock InfoTitleText = null!;

    private TextBlock FullscreenText = null!;

    private Button EmptyOpenFolderButton = null!;

    private Button EmptyOpenImageButton = null!;

    private Button ZoomButton = null!;

    private Button ZoomInButton = null!;

    private Button ZoomOutButton = null!;

    private Button FitButton = null!;

    private Button ActualButton = null!;

    private Button RotateButton = null!;

    private Button EditButton = null!;

    private Button InfoToggle = null!;

    private Button FilmstripToggle = null!;

    private Button DualViewButton = null!;

    private Button SlideshowButton = null!;

    private Button FullscreenButton = null!;

    private Button SettingsButton = null!;

    private Button PrevButton = null!;

    private Button NextButton = null!;

    private Button ThemeButton = null!;

    private Button MinimizeButton = null!;

    private Button MaximizeButton = null!;

    private Button CloseButton = null!;

    private IconPath MaximizeIcon = null!;

    private TextBlock EmptyOpenFolderText = null!;

    private TextBlock EmptyOpenImageText = null!;

    public MainWindow(
        AppServices services,
        CommandLineOptions options,
        BenchmarkRecorder? benchmark = null,
        Task<PreloadedImage?>? preload = null)
    {
        _services = services;
        _options = options;
        _benchmark = benchmark;

        StartupTrace.Mark("window-ctor-enter");
        BuildUi();
        StartupTrace.Mark("window-ctor-ui");

        _viewModel = new MainWindowViewModel(services, preload);
        DataContext = _viewModel;
        SizeChanged += (_, _) => UpdateTopBarLayout();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ImagePresented += OnImagePresented;
        _viewModel.TiledImagePresented += OnTiledImagePresented;
        _viewModel.TiledImageCleared += OnTiledImageCleared;
        Viewer.PickHover += OnViewerPickHover;
        Viewer.PickRequested += OnViewerPickRequested;
        _viewModel.CurrentItemChanged += (_, _) => _filmstripView?.ScrollTo(_viewModel.CurrentIndex);
        StartupTrace.Mark("window-ctor-vm");

        Width = benchmark is null
            ? Math.Clamp(services.Settings.WindowWidth, 720, 8000)
            : 1240;
        Height = benchmark is null
            ? Math.Clamp(services.Settings.WindowHeight, 480, 8000)
            : 860;

        ConfigureWindowChrome();
        UpdateMaximizeIcon();
        ApplyLanguage();
        ApplyViewModelState();
        StartupTrace.Mark("window-ctor-chrome");

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cursorTimer.Tick += OnCursorTimerTick;
        _chromeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _chromeTimer.Tick += OnChromeTimerTick;

        if (benchmark?.Mode == BenchmarkMode.Startup)
        {
            _benchmarkTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _benchmarkTimeout.Tick += (_, _) =>
            {
                _benchmarkTimeout.Stop();
                benchmark!.Fail("first frame timed out");
            };
        }

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        // Tunnel, not bubble: the filmstrip ListBox would otherwise consume arrow keys
        // before the window can turn them into previous/next navigation.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        CanvasBorder.PointerPressed += OnCanvasPointerPressed;
        _services.Localization.LanguageChanged += OnLocalizationChanged;
        _services.Theme.Changed += OnThemeChanged;

        PointerMoved += OnWindowPointerMoved;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                UpdateMaximizeIcon();
                if (WindowState == WindowState.Minimized)
                {
                    _cursorTimer.Stop();
                }
            }
        };

        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        StartupTrace.Mark("window-ctor-exit");
    }

    /// <summary>
    /// Properties the viewer raises on a timer or on every pointer move. Applying the
    /// whole window state per notification cost two full passes per zoom-animation
    /// frame (8 ms apart) and one per crop-drag move, so these are coalesced into a
    /// single pass per dispatcher turn. Every other property still applies
    /// synchronously, because a mode change must not lag a frame.
    /// </summary>
    private static readonly HashSet<string> CoalescedProperties = new(StringComparer.Ordinal)
    {
        nameof(MainWindowViewModel.ZoomPercent),
        nameof(MainWindowViewModel.ZoomText),
        nameof(MainWindowViewModel.Crop)
    };

    private bool _stateApplyQueued;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && CoalescedProperties.Contains(name))
        {
            QueueViewModelState();
            return;
        }

        ApplyViewModelState();
    }

    private void QueueViewModelState()
    {
        if (_stateApplyQueued)
        {
            return;
        }

        _stateApplyQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _stateApplyQueued = false;
                ApplyViewModelState();
            },
            DispatcherPriority.Render);
    }

    /// <summary>
    /// Opens a path handed over by a second launch, so the caller's process can exit.
    /// The view model already guards against a load superseding another one.
    /// </summary>
    public void OpenForwardedPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        ShowChrome();
        _ = _viewModel.OpenPathAsync(paths[0]);
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e) =>
        ToggleFullscreen();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Benchmarks must not change the settings that the next run will load.
        if (_benchmark is not null)
        {
            return;
        }

        if (!_isFullscreen && WindowState != WindowState.Minimized)
        {
            _services.Settings.WindowWidth = Bounds.Width;
            _services.Settings.WindowHeight = Bounds.Height;
            _services.Settings.WindowMaximized = WindowState == WindowState.Maximized;
        }

        if (_services.Settings.RememberZoom)
        {
            _services.Settings.ZoomMode = Viewer.ZoomMode switch
            {
                ViewerZoomMode.FitWidth => "fit-width",
                ViewerZoomMode.Actual => "actual",
                ViewerZoomMode.Manual => "manual",
                _ => "fit"
            };
            _services.Settings.ZoomFactor = Viewer.ZoomFactor;
        }

        _services.SaveSettings();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _services.Localization.LanguageChanged -= OnLocalizationChanged;
        _services.Theme.Changed -= OnThemeChanged;
        _cursorTimer.Stop();
        _chromeTimer.Stop();
        _benchmarkTimeout?.Stop();
        Viewer.ClearImage();
        _dualView?.Dispose();
        _dualView = null;
        _viewModel.Dispose();
    }

}

