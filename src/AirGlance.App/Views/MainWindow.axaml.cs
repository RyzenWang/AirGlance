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

    private void BuildUi()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 720;
        MinHeight = 480;
        Title = "AirGlance";
        Classes.Add("shell");

        _root = new Grid();
        var root = _root;

        // The image owns the whole window. Overlay chrome floats above it.
        CanvasBorder = new Border
        {
            ClipToBounds = true,
            Padding = new Thickness(56, 42, 56, 58)
        };
        CanvasBorder.Classes.Add("canvas");

        var canvasGrid = new Grid();
        Viewer.FirstFramePresented += OnFirstFramePresented;
        Viewer.ZoomChanged += OnViewerZoomChanged;
        Viewer.ViewportChanged += OnViewerViewportChanged;
        canvasGrid.Children.Add(Viewer);

        CropControl = new CropOverlay(Viewer);
        CropControl.SetValue(Panel.ZIndexProperty, 20);
        CropControl.CropChanged += OnCropChanged;
        canvasGrid.Children.Add(CropControl);

        TileLoadingChip = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#E60A0E13")),
            BorderBrush = new SolidColorBrush(Color.Parse("#24FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 10)
        };
        TileLoadingChip.SetValue(Panel.ZIndexProperty, 30);
        TileLoadingText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12
        };
        TileLoadingChip.Child = TileLoadingText;
        canvasGrid.Children.Add(TileLoadingChip);

        LoadingChip = new Border();
        LoadingChip.Classes.Add("loading-chip");
        LoadingText = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
        LoadingChip.Child = LoadingText;
        canvasGrid.Children.Add(LoadingChip);

        CanvasBorder.Child = canvasGrid;
        CanvasBorder.AddHandler(
            PointerPressedEvent,
            OnCropCanvasPointerPressed,
            RoutingStrategies.Tunnel);
        CanvasBorder.AddHandler(
            PointerMovedEvent,
            OnCropCanvasPointerMoved,
            RoutingStrategies.Tunnel);
        CanvasBorder.AddHandler(
            PointerReleasedEvent,
            OnCropCanvasPointerReleased,
            RoutingStrategies.Tunnel);
        root.Children.Add(CanvasBorder);

        // Empty / error card.
        EmptyCard = new Border { IsVisible = false };
        EmptyCard.Classes.Add("empty-card");
        EmptyCard.SetValue(Panel.ZIndexProperty, 20);

        var emptyStack = new StackPanel { Spacing = 0 };
        var emptyIcon = new Border();
        emptyIcon.Classes.Add("empty-icon");
        var emptyGlyph = new IconPath
        {
            Data = FindGeometry("IconFolderPlus"),
            Width = 27,
            Height = 27
        };
        emptyGlyph.Classes.Add("empty-glyph");
        emptyIcon.Child = emptyGlyph;
        emptyStack.Children.Add(emptyIcon);

        OverlayTitle = new TextBlock();
        OverlayTitle.Classes.Add("empty-title");
        emptyStack.Children.Add(OverlayTitle);

        OverlayMessage = new TextBlock { Margin = new Thickness(0, 9, 0, 0) };
        OverlayMessage.Classes.Add("empty-message");
        emptyStack.Children.Add(OverlayMessage);

        OverlayHint = new TextBlock { Margin = new Thickness(0, 5, 0, 0) };
        OverlayHint.Classes.Add("empty-hint");
        emptyStack.Children.Add(OverlayHint);

        var emptyActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Margin = new Thickness(0, 22, 0, 0)
        };
        EmptyOpenFolderButton = new Button();
        EmptyOpenFolderButton.Classes.Add("empty-primary");
        EmptyOpenFolderText = AttachEmptyActionIcon(EmptyOpenFolderButton, "IconFolder");
        EmptyOpenFolderButton.Click += OnOpenFolderClick;
        EmptyOpenImageButton = new Button();
        EmptyOpenImageButton.Classes.Add("empty-secondary");
        EmptyOpenImageText = AttachEmptyActionIcon(EmptyOpenImageButton, "IconImage");
        EmptyOpenImageButton.Click += OnOpenImageClick;
        emptyActions.Children.Add(EmptyOpenFolderButton);
        emptyActions.Children.Add(EmptyOpenImageButton);
        emptyStack.Children.Add(emptyActions);

        var shortcutHint = new TextBlock
        {
            Text = "Ctrl+O  \u00B7  Ctrl+Shift+O",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0)
        };
        shortcutHint.Classes.Add("empty-shortcut");
        emptyStack.Children.Add(shortcutHint);

        EmptyCard.Child = emptyStack;
        root.Children.Add(EmptyCard);

        // Top chrome: folder chip, file chip, window controls.
        TopBar = new Border
        {
            Height = 38,
            Margin = new Thickness(18, 16, 18, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent
        };
        TopBar.PointerPressed += OnTitleBarPointerPressed;
        TopBar.SetValue(Panel.ZIndexProperty, 40);

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        FolderChip = new Border { IsVisible = false, MaxWidth = 300 };
        FolderChip.Classes.Add("overlay-chip");
        var folderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center
        };
        folderRow.Children.Add(new IconPath
        {
            Data = FindGeometry("IconFolder"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            Width = 15,
            Height = 15
        });
        FolderNameText = new TextBlock();
        FolderNameText.Classes.Add("chrome-name");
        FolderNameText.MaxWidth = 180;
        folderRow.Children.Add(FolderNameText);
        FolderCountText = new TextBlock();
        FolderCountText.Classes.Add("chrome-meta");
        folderRow.Children.Add(FolderCountText);
        FolderChip.Child = folderRow;
        topGrid.Children.Add(FolderChip);

        BrandChip = new Border { IsVisible = false };
        BrandChip.Classes.Add("overlay-chip");
        var brandRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        var brandMark = new Border();
        brandMark.Classes.Add("chrome-icon");
        brandRow.Children.Add(brandMark);
        var brandName = new TextBlock { Text = "AirGlance" };
        brandName.Classes.Add("chrome-name");
        brandRow.Children.Add(brandName);
        BrandChip.Child = brandRow;
        topGrid.Children.Add(BrandChip);

        FileChip = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520
        };
        FileChip.Classes.Add("overlay-chip");
        var fileRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,54"),
            VerticalAlignment = VerticalAlignment.Center
        };
        FileNameText = new TextBlock { MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
        FileNameText.Classes.Add("chrome-file");
        fileRow.Children.Add(FileNameText);
        var fileSeparator = new Border
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(10, 0),
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(fileSeparator, 1);
        fileRow.Children.Add(fileSeparator);
        FileMetaText = new TextBlock
        {
            MaxWidth = 300,
            Margin = new Thickness(0, 0, 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        FileMetaText.Classes.Add("chrome-meta");
        Grid.SetColumn(FileMetaText, 2);
        fileRow.Children.Add(FileMetaText);
        FilePositionText = new TextBlock
        {
            Width = 54,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.Parse("#F4A593"))
        };
        FilePositionText.Classes.Add("chrome-meta");
        Grid.SetColumn(FilePositionText, 3);
        fileRow.Children.Add(FilePositionText);
        FileChip.Child = fileRow;
        Grid.SetColumn(FileChip, 0);
        Grid.SetColumnSpan(FileChip, 3);
        topGrid.Children.Add(FileChip);

        WindowChip = new Border { IsVisible = OperatingSystem.IsWindows(), Padding = new Thickness(4) };
        WindowChip.Classes.Add("overlay-chip");
        CaptionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        ThemeButton = CreateWindowButton("IconTheme", "L.ToggleTheme", OnThemeClick, iconSize: 14);
        MinimizeButton = CreateWindowButton("IconMinimize", "L.Minimize", OnMinimizeClick, iconSize: 14);
        MaximizeButton = CreateWindowButton("IconMaximize", "L.Maximize", OnMaximizeClick, iconSize: 13);
        MaximizeIcon = (IconPath)MaximizeButton.Content!;
        CloseButton = CreateWindowButton("IconClose", "L.Close", OnCloseClick, iconSize: 14, close: true);
        CaptionButtons.Children.Add(ThemeButton);
        CaptionButtons.Children.Add(MinimizeButton);
        CaptionButtons.Children.Add(MaximizeButton);
        CaptionButtons.Children.Add(CloseButton);
        WindowChip.Child = CaptionButtons;
        WindowChip.HorizontalAlignment = HorizontalAlignment.Right;
        WindowChip.VerticalAlignment = VerticalAlignment.Top;
        WindowChip.Margin = new Thickness(0, 16, 18, 0);
        WindowChip.SetValue(Panel.ZIndexProperty, 60);
        root.Children.Add(WindowChip);

        TopBar.Child = topGrid;
        root.Children.Add(TopBar);

        // Side navigation. Each button gets its own 46 x 46 host so the layer that
        // fades never covers the canvas, while the buttons keep their resting opacity.
        PrevButton = CreateNavButton("IconChevronLeft", "L.PreviousImage", OnPreviousClick);
        NextButton = CreateNavButton("IconChevronRight", "L.NextImage", OnNextClick);

        PrevHost = new Grid
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22, 0, 0, 0),
            IsHitTestVisible = false
        };
        NextHost = new Grid
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 22, 0),
            IsHitTestVisible = false
        };
        PrevHost.Children.Add(PrevButton);
        NextHost.Children.Add(NextButton);
        PrevHost.SetValue(Panel.ZIndexProperty, 35);
        NextHost.SetValue(Panel.ZIndexProperty, 35);
        root.Children.Add(PrevHost);
        root.Children.Add(NextHost);

        // Bottom dock.
        Dock = new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 22)
        };
        Dock.Classes.Add("dock");
        Dock.SetValue(Panel.ZIndexProperty, 50);

        var dockRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        };

        ZoomOutButton = CreateToolButton("IconMinus", "L.ZoomOut", (_, _) => Viewer.ZoomOut());
        dockRow.Children.Add(ZoomOutButton);

        DockZoomText = new TextBlock { Text = "100%" };
        DockZoomText.Classes.Add("dock-zoom");
        ZoomButton = new Button
        {
            Content = DockZoomText,
            Width = 48,
            Height = 40,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        ZoomButton.Click += OnZoomPresetClick;
        dockRow.Children.Add(ZoomButton);

        ZoomInButton = CreateToolButton("IconPlus", "L.ZoomIn", (_, _) => Viewer.ZoomIn());
        dockRow.Children.Add(ZoomInButton);

        dockRow.Children.Add(CreateDockDivider());
        FitButton = CreateToolButton("IconFit", "L.FitWindow", (_, _) => Viewer.AnimateZoomMode(ViewerZoomMode.FitWindow));
        dockRow.Children.Add(FitButton);

        ActualButton = new Button
        {
            Content = new TextBlock
            {
                Text = "1:1",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        ((TextBlock)ActualButton.Content).Classes.Add("tool-text");
        ActualButton.Classes.Add("tool");
        ActualButton.Classes.Add("wide");
        ActualButton.Click += (_, _) => Viewer.AnimateZoomMode(ViewerZoomMode.Actual);
        dockRow.Children.Add(ActualButton);

        dockRow.Children.Add(CreateDockDivider());
        RotateButton = CreateToolButton(
            "IconRotate",
            "L.RotateClockwise",
            async (_, _) => await _viewModel.RotateClockwiseAsync());
        dockRow.Children.Add(RotateButton);

        InfoToggle = CreateToolButton("IconInfo", "L.InfoPanel", OnInfoToggleClick);
        dockRow.Children.Add(InfoToggle);

        FilmstripToggle = CreateToolButton("IconFilmstrip", "L.Filmstrip", OnFilmstripToggleClick);
        dockRow.Children.Add(FilmstripToggle);

        DualViewButton = CreateToolButton(
            "IconSplitView",
            "L.DualView",
            (_, _) => _ = ToggleDualAsync());
        dockRow.Children.Add(DualViewButton);

        SlideshowButton = CreateToolButton("IconPlay", "L.Slideshow", (_, _) => _viewModel.ToggleSlideshow());
        dockRow.Children.Add(SlideshowButton);

        dockRow.Children.Add(CreateDockDivider());
        EditButton = CreateToolButton("IconEdit", "L.EditHint", OnEditClick);
        dockRow.Children.Add(EditButton);
        FullscreenButton = CreateToolButton("IconFullscreen", "L.Fullscreen", OnFullscreenClick);
        dockRow.Children.Add(FullscreenButton);

        SettingsButton = CreateToolButton("IconSettings", "L.Settings", OnSettingsClick);
        dockRow.Children.Add(SettingsButton);

        Dock.Child = dockRow;
        root.Children.Add(Dock);

        // Filmstrip is an overlay drawer, not a layout row.
        FilmstripHost = new Border { IsVisible = false };
        FilmstripHost.Classes.Add("filmstrip-overlay");
        FilmstripHost.SetValue(Panel.ZIndexProperty, 30);
        root.Children.Add(FilmstripHost);

        // Information drawer.
        InfoHost = new Border { IsVisible = false };
        InfoHost.Classes.Add("info-overlay");
        InfoHost.SetValue(Panel.ZIndexProperty, 36);

        var infoGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var infoHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(20, 18, 10, 4)
        };
        InfoTitleText = new TextBlock();
        InfoTitleText.Classes.Add("info-title");
        infoHeader.Children.Add(InfoTitleText);
        var infoClose = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry("IconClose"),
                Stroke = new SolidColorBrush(Color.Parse("#9A9286")),
                StrokeThickness = 1.7,
                Width = 15,
                Height = 15
            },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        infoClose.Click += OnInfoToggleClick;
        Grid.SetColumn(infoClose, 1);
        infoHeader.Children.Add(infoClose);
        infoGrid.Children.Add(infoHeader);

        InfoContentHost = new ContentControl();
        Grid.SetRow(InfoContentHost, 1);
        infoGrid.Children.Add(InfoContentHost);
        InfoHost.Child = infoGrid;
        root.Children.Add(InfoHost);

        // Analysis drawer. Same geometry as the information drawer; only one of the
        // two is visible at a time.
        AnalysisHost = new Border { IsVisible = false };
        AnalysisHost.Classes.Add("analysis-drawer");
        AnalysisHost.SetValue(Panel.ZIndexProperty, 36);
        AnalysisContentHost = new ContentControl();
        AnalysisHost.Child = AnalysisContentHost;
        root.Children.Add(AnalysisHost);

        EditPanel = new EditPanelView(_services.Localization)
        {
            IsVisible = false
        };
        EditPanel.CloseRequested += OnEditCloseRequested;
        EditPanel.CancelRequested += OnEditCloseRequested;
        EditPanel.ExportRequested += OnEditExportRequested;
        EditPanel.ResetRequested += OnEditResetRequested;
        EditPanel.TransformRequested += OnEditTransformRequested;
        EditPanel.CropRequested += OnEditCropRequested;
        root.Children.Add(EditPanel);

        CropBar = new CropToolbarView(_services.Localization) { IsVisible = false };
        CropBar.SetValue(Panel.ZIndexProperty, 55);
        CropBar.ResetRequested += OnCropBarResetRequested;
        CropBar.CancelRequested += OnCropBarCancelRequested;
        CropBar.DoneRequested += OnCropBarDoneRequested;
        CropBar.AspectRequested += OnEditCropAspectRequested;
        root.Children.Add(CropBar);

        CropModeTag = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24, 22, 0, 0),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(178, 15, 12, 10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = _services.Localization.T("L.EditCropTag"),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 0.8,
                Foreground = new SolidColorBrush(Color.Parse("#F5EFE8"))
            }
        };
        CropModeTag.SetValue(Panel.ZIndexProperty, 55);
        root.Children.Add(CropModeTag);

        CropSafeTag = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 22, 24, 0),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(178, 15, 12, 10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = _services.Localization.T("L.EditSafeTag"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.Parse("#D8D2CA"))
            }
        };
        CropSafeTag.SetValue(Panel.ZIndexProperty, 55);
        root.Children.Add(CropSafeTag);

        // Transient toast.
        ToastBorder = new Border { IsVisible = false };
        ToastBorder.Classes.Add("toast");
        TransientMessageText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#F7F3EA")),
            FontSize = 11
        };
        ToastBorder.Child = TransientMessageText;
        ToastBorder.SetValue(Panel.ZIndexProperty, 55);
        root.Children.Add(ToastBorder);

        // Drop hint on top of everything.
        DragOverlay = new Border { IsVisible = false };
        DragOverlay.Classes.Add("dragover");
        DragOverlay.SetValue(Panel.ZIndexProperty, 60);
        var dropHintBorder = new Border
        {
            Padding = new Thickness(18, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        dropHintBorder.Classes.Add("panel");
        DropHintText = new TextBlock();
        DropHintText.Classes.Add("drop-hint");
        dropHintBorder.Child = DropHintText;
        DragOverlay.Child = dropHintBorder;
        root.Children.Add(DragOverlay);

        // Fullscreen keeps a single quiet hint in the bottom-right corner.
        FullscreenHint = new Border { IsVisible = false };
        FullscreenHint.Classes.Add("fullscreen-hint");
        FullscreenHint.Margin = new Thickness(0, 0, 22, 18);
        FullscreenHint.SetValue(Panel.ZIndexProperty, 50);
        FullscreenText = new TextBlock();
        FullscreenText.Classes.Add("fullscreen-hint-text");
        FullscreenHint.Child = FullscreenText;
        root.Children.Add(FullscreenHint);

        Content = root;
    }

    private Button CreateToolButton(
        string iconResource,
        string tooltipKey,
        EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(iconResource),
                Width = 18,
                Height = 18
            }
        };
        button.Classes.Add("tool");
        button.Click += handler;
        button.Tag = tooltipKey;
        return button;
    }

    private Button CreateNavButton(
        string iconResource,
        string tooltipKey,
        EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(iconResource),
                Width = 19,
                Height = 19
            }
        };
        button.Classes.Add("nav-button");
        button.Click += handler;
        button.Tag = tooltipKey;
        return button;
    }

    private Button CreateWindowButton(
        string iconResource,
        string automationKey,
        EventHandler<RoutedEventArgs> clickHandler,
        double iconSize,
        bool close = false)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(iconResource),
                Width = iconSize,
                Height = iconSize
            }
        };
        button.Classes.Add("window-tool");
        if (close)
        {
            button.Classes.Add("close");
        }

        button.Click += clickHandler;
        button.Tag = automationKey;
        return button;
    }

    private TextBlock AttachEmptyActionIcon(Button button, string iconResource)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new IconPath
        {
            Data = FindGeometry(iconResource),
            Width = 15,
            Height = 15
        });
        row.Children.Add(label);
        button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        return label;
    }

    private Border CreateDockDivider()
    {
        var divider = new Border();
        divider.Classes.Add("dock-divider");
        return divider;
    }

    private Geometry FindGeometry(string key) =>
        this.FindResource(key) as Geometry ?? StreamGeometry.Parse("M 0 0");

    private void ApplyLanguage()
    {
        LoadingText.Text = _services.Localization.T("L.Loading");
        TileLoadingText.Text = _viewModel.RegionLoadingText;
        DropHintText.Text = _services.Localization.T("L.DropHint");
        AutomationProperties.SetName(MinimizeButton, _services.Localization.T("L.Minimize"));
        AutomationProperties.SetName(MaximizeButton, _services.Localization.T("L.Maximize"));
        AutomationProperties.SetName(CloseButton, _services.Localization.T("L.Close"));

        UpdateThemeButton();
        ApplyTool(ZoomButton, "L.ZoomLevel");
        ApplyTool(ZoomOutButton, "L.ZoomOut");
        ApplyTool(ZoomInButton, "L.ZoomIn");
        ApplyTool(FitButton, "L.FitWindow");
        ApplyTool(ActualButton, "L.ActualSize");
        ApplyTool(RotateButton, "L.RotateClockwise");
        ApplyTool(EditButton, "L.EditHint");
        ApplyTool(InfoToggle, "L.InfoPanel");
        ApplyTool(FilmstripToggle, "L.Filmstrip");
        ApplyTool(DualViewButton, "L.DualView");
        ApplyTool(SlideshowButton, "L.Slideshow");
        ApplyTool(FullscreenButton, "L.Fullscreen");
        ApplyTool(SettingsButton, "L.Settings");
        ApplyTool(PrevButton, "L.PreviousImage");
        ApplyTool(NextButton, "L.NextImage");
        EditPanel.ApplyLanguage();

        InfoTitleText.Text = _services.Localization.T("L.InfoPanelShort");
        FullscreenText.Text = _services.Localization.T("L.FullscreenHint");
        EmptyOpenFolderText.Text = _services.Localization.T("L.OpenFolder").TrimEnd('…');
        EmptyOpenImageText.Text = _services.Localization.T("L.OpenFile").TrimEnd('…');
    }

    private void ApplyTool(Button button, string key)
    {
        var text = _services.Localization.T(key);
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
        CropControl.RefreshTheme();
    }

    private void UpdateThemeButton()
    {
        var dark = _services.Theme.IsDark;
        if (ThemeButton.Content is IconPath icon)
        {
            icon.Data = FindGeometry(dark ? "IconMoon" : "IconTheme");
        }

        ApplyTool(ThemeButton, dark ? "L.SwitchToLight" : "L.SwitchToDark");
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

    private void ApplyViewModelState()
    {
        Title = _viewModel.WindowTitle;
        FolderNameText.Text = _viewModel.FolderName;
        UpdateTopBarLayout();
        FolderCountText.Text = _viewModel.Images.Count > 0
            ? _viewModel.Images.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        FileChip.IsVisible = _viewModel.HasImage;
        FileNameText.Text = _viewModel.TitleFileName;
        var meta = new List<string>(2);
        if (!string.IsNullOrEmpty(_viewModel.StatusDimensions))
        {
            meta.Add(_viewModel.StatusDimensions);
        }

        if (!string.IsNullOrEmpty(_viewModel.ZoomText))
        {
            meta.Add(_viewModel.ZoomText);
        }

        FileMetaText.Text = string.Join(" · ", meta);
        var count = _viewModel.Images.Count;
        FilePositionText.Text = count == 0
            ? string.Empty
            : $"{_viewModel.CurrentIndex + 1:D2} / {count:D2}";

        DockZoomText.Text = _viewModel.ZoomText;
        EmptyCard.IsVisible = _viewModel.OverlayVisible;
        OverlayTitle.Text = _viewModel.OverlayTitle;
        OverlayMessage.Text = _viewModel.OverlayMessage;
        OverlayMessage.IsVisible = !string.IsNullOrEmpty(_viewModel.OverlayMessage);
        OverlayHint.Text = _viewModel.OverlayHint;
        OverlayHint.IsVisible = !string.IsNullOrEmpty(_viewModel.OverlayHint);

        LoadingChip.IsVisible = _viewModel.IsLoading;
        TileLoadingText.Text = _viewModel.RegionLoadingText;
        TileLoadingChip.IsVisible = _viewModel.ShowRegionLoading;
        InfoHost.IsVisible = _viewModel.InfoShown && !_cropMode;
        InfoHost.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 82, 22, 150)
            : new Thickness(0, 82, 22, 92);
        AnalysisHost.IsVisible = _viewModel.AnalysisShown && !_cropMode && !_viewModel.IsEditing;
        AnalysisHost.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 82, 22, 150)
            : new Thickness(0, 82, 22, 92);
        if (AnalysisHost.IsVisible)
        {
            EnsureAnalysisPanel();
        }
        Viewer.IsPicking = _viewModel.Analysis.IsEyedropperActive &&
                           _viewModel.AnalysisShown &&
                           !_viewModel.IsEditing &&
                           !_dualMode &&
                           !_cropMode;
        EditPanel.IsVisible = _viewModel.IsEditing && !_cropMode;
        EditPanel.Margin = new Thickness(0, 82, 22, 0);
        CropBar.IsVisible = _viewModel.IsEditing && _cropMode;
        CropModeTag.IsVisible = _viewModel.IsEditing && _cropMode;
        CropSafeTag.IsVisible = _viewModel.IsEditing && _cropMode;
        CropControl.IsActive = _viewModel.IsEditing && _cropMode;
        if (_viewModel.IsEditing && !_cropDragging && CropControl.Crop != _viewModel.Crop)
        {
            CropControl.SetCrop(_viewModel.Crop);
            UpdateCropUi();
        }

        EditButton.IsEnabled = _viewModel.HasImage && !_dualMode && _viewModel.CanEdit;
        EditButton.Classes.Set("active", _viewModel.IsEditing);
        var editTip = _dualMode
            ? _services.Localization.T("L.EditDualUnavailable")
            : _viewModel.IsProxyOnly
                ? _services.Localization.T("L.EditNeedsFullResolution")
                : _viewModel.IsTiled
                    ? _services.Localization.T("L.EditUnavailable")
                    : _services.Localization.T("L.EditHint");
        ToolTip.SetTip(EditButton, editTip);
        FilmstripHost.IsVisible = _viewModel.FilmstripShown && !_cropMode;
        Dock.IsVisible = !_viewModel.OverlayVisible && !_cropMode;
        Dock.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 0, 0, 150)
            : new Thickness(0, 0, 0, 22);

        var showNavigation = _viewModel.HasImage && count > 1 && !_viewModel.OverlayVisible && !_cropMode;
        PrevButton.IsVisible = showNavigation;
        NextButton.IsVisible = showNavigation;

        InfoToggle.Classes.Set("active", _viewModel.InfoShown);
        FilmstripToggle.Classes.Set("active", _viewModel.FilmstripShown);
        FullscreenButton.Classes.Set("active", _viewModel.IsFullscreen);
        TopBar.IsVisible = !_viewModel.IsFullscreen && !_cropMode;

        TransientMessageText.Text = _viewModel.TransientMessage ?? string.Empty;
        ToastBorder.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.TransientMessage);
        DragOverlay.IsVisible = _viewModel.IsDragOver;

        CanvasBorder.Padding = _cropMode
            ? new Thickness(32, 80, 32, 150)
            : _viewModel.IsFullscreen
                ? new Thickness(0)
                : new Thickness(56, 42, 56, 58);
        if (_viewModel.IsFullscreen)
        {
            if (!CanvasBorder.Classes.Contains("fullscreen"))
            {
                CanvasBorder.Classes.Add("fullscreen");
            }
        }
        else
        {
            CanvasBorder.Classes.Remove("fullscreen");
        }

        RefreshChromeVisibility();

        if (_dualMode)
        {
            CanvasBorder.IsVisible = false;
            EmptyCard.IsVisible = false;
            TopBar.IsVisible = false;
            Dock.IsVisible = false;
            PrevHost.IsVisible = false;
            NextHost.IsVisible = false;
            FilmstripHost.IsVisible = false;
            InfoHost.IsVisible = false;
            AnalysisHost.IsVisible = false;
            FullscreenHint.IsVisible = false;
            DragOverlay.IsVisible = false;
            WindowChip.IsVisible = OperatingSystem.IsWindows() && !_viewModel.IsFullscreen;
            if (_dualView is not null)
            {
                _dualView.IsVisible = true;
            }

            return;
        }

        if (_dualView is not null)
        {
            _dualView.IsVisible = false;
        }

        CanvasBorder.IsVisible = true;
        WindowChip.IsVisible = OperatingSystem.IsWindows() && !_viewModel.IsFullscreen && !_cropMode;
    }

    private void UpdateTopBarLayout()
    {
        var compact = Bounds.Width > 0 && Bounds.Width < 900;
        var hasFolder = !string.IsNullOrWhiteSpace(_viewModel.FolderName);
        FolderChip.IsVisible = !compact && hasFolder;
        BrandChip.IsVisible = !hasFolder;
        FileChip.MaxWidth = compact ? Math.Max(220, Bounds.Width * 0.62) : 520;
    }

    private void ShowChrome()
    {
        _chromeVisible = true;
        RefreshChromeVisibility();
        _chromeTimer.Stop();
        _chromeTimer.Start();
    }

    private void HideChrome()
    {
        _chromeVisible = false;
        RefreshChromeVisibility();
    }

    private void RefreshChromeVisibility()
    {
        if (_viewModel is null)
        {
            return;
        }

        var empty = _viewModel.OverlayVisible;
        var topVisible = _chromeVisible || empty;
        TopBar.IsVisible = !_viewModel.IsFullscreen && !_cropMode;
        TopBar.Opacity = topVisible ? 1 : 0;
        TopBar.IsHitTestVisible = topVisible;

        var controlsVisible = _chromeVisible && !empty;
        Dock.Opacity = controlsVisible && !_cropMode ? 1 : 0;
        Dock.IsHitTestVisible = controlsVisible && !_cropMode;
        var navVisible = controlsVisible && !_viewModel.IsFullscreen && !_cropMode;
        PrevHost.Opacity = navVisible ? 1 : 0;
        NextHost.Opacity = navVisible ? 1 : 0;
        PrevHost.IsHitTestVisible = navVisible;
        NextHost.IsHitTestVisible = navVisible;

        // In fullscreen the hint appears once the dock has faded out, so the two
        // never share the bottom-right corner on a narrow screen.
        FullscreenHint.IsVisible = _viewModel.IsFullscreen && !_chromeVisible && !_cropMode;
    }

    private void OnChromeTimerTick(object? sender, EventArgs e)
    {
        _chromeTimer.Stop();
        if (_viewModel.OverlayVisible)
        {
            return;
        }

        if (TopBar.IsPointerOver || Dock.IsPointerOver ||
            PrevButton.IsPointerOver || NextButton.IsPointerOver ||
            InfoHost.IsPointerOver || FilmstripHost.IsPointerOver)
        {
            _chromeTimer.Start();
            return;
        }

        HideChrome();
    }

    private void ConfigureWindowChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            // BorderOnly keeps the native resize border while the caption row is ours.
            SystemDecorations = SystemDecorations.BorderOnly;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 44;
            CaptionButtons.IsVisible = true;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            SystemDecorations = SystemDecorations.Full;
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        StartupTrace.Mark("window-opened");
        _benchmark?.MarkWindowShown();
        ShowChrome();
        if (_benchmarkTimeout is not null)
        {
            _benchmarkTimeout.Start();
        }

        if (_services.Settings.WindowMaximized &&
            _benchmark?.Mode != BenchmarkMode.Startup)
        {
            WindowState = WindowState.Maximized;
        }
        try
        {
            if (_options.ShowHelp || _options.ShowVersion)
            {
                await ShowCommandLineMessageAsync();
                return;
            }

            await _viewModel.InitializeAsync(_options.TargetPath);
            StartupTrace.Mark("image-initialized");
            Dispatcher.UIThread.Post(EnsurePanels, DispatcherPriority.Background);

            if (_benchmark?.Mode == BenchmarkMode.Startup)
            {
                if (!_viewModel.HasImage)
                {
                    _benchmark.Fail(_viewModel.OverlayMessage.Length > 0
                        ? _viewModel.OverlayMessage
                        : "no image");
                }
                else if (_options.TargetPath is null)
                {
                    _benchmark.Fail("no image path");
                }
            }
        }
        catch (Exception exception)
        {
            _benchmark?.Fail(exception.Message);
        }
    }

    private void OnTiledImagePresented(object? sender, TiledImagePresentedEventArgs e) =>
        Viewer.SetTiles(e.Tiles);

    private void OnTiledImageCleared(object? sender, EventArgs e) =>
        Viewer.SetTiles(null);

    private void OnImagePresented(object? sender, ImagePresentedEventArgs e)
    {
        StartupTrace.Mark("image-presented");
        if (e.Animation is { } animation)
        {
            Viewer.SetAnimation(animation, e.ResetView);
        }
        else if (e.Bitmap is not null)
        {
            Viewer.SetImage(
                e.Bitmap,
                e.SourceWidth,
                e.SourceHeight,
                e.ImageScale,
                e.ResetView,
                e.Animate);
        }
        else
        {
            Viewer.ClearImage();
            return;
        }

        if (!_initialZoomApplied)
        {
            _initialZoomApplied = true;
            Viewer.ApplySavedZoom(_viewModel.InitialZoomMode, _viewModel.InitialZoomFactor);
        }
        else if (e.IsNavigation && !_viewModel.RememberZoom)
        {
            Viewer.SetZoomMode(ViewerZoomMode.FitWindow);
        }

        _viewModel.OnZoomChanged(Viewer.ZoomPercent);
        ShowChrome();
    }

    private void OnViewerZoomChanged(object? sender, ZoomChangedEventArgs e)
    {
        _viewModel.OnZoomChanged(e.Percent);
        CropControl.InvalidateVisual();
    }

    private void OnViewerViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        _viewModel.SetViewport(e.PixelWidth, e.PixelHeight);
        CropControl.InvalidateVisual();
    }

    private void OnFirstFramePresented(object? sender, EventArgs e)
    {
        _benchmarkTimeout?.Stop();

        // The window is the product; the filmstrip and information panel arrive one
        // frame later, after the pixels are already on screen.
        Dispatcher.UIThread.Post(EnsurePanels, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(SetWindowIcon, DispatcherPriority.Background);

        if (_benchmark is not null)
        {
            _benchmark.MarkFirstFrame(
                _viewModel.LastDecodeMilliseconds,
                _viewModel.Metadata?.Megapixels ?? 0);
        }
    }

    private void SetWindowIcon()
    {
        if (Icon is not null)
        {
            return;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://AirGlance/Assets/airglance.png"));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // An icon is cosmetic; a failure must not affect the window.
        }
    }

    private void EnsurePanels()
    {
        if (_panelsCreated)
        {
            return;
        }

        _panelsCreated = true;
        _filmstripView = new FilmstripView();
        FilmstripHost.Child = _filmstripView;
        _filmstripView.ScrollTo(_viewModel.CurrentIndex);

        _infoPanel = new InfoPanelView();
        InfoContentHost.Content = _infoPanel;
    }

    private void EnsureAnalysisPanel()
    {
        if (AnalysisContentHost.Content is not null)
        {
            return;
        }

        _analysisPanel = new AnalysisPanelView(_viewModel.Analysis);
        AnalysisContentHost.Content = _analysisPanel;
        _viewModel.Analysis.SamplesChanged += OnAnalysisSamplesChanged;
        _viewModel.Analysis.CloseRequested += OnAnalysisCloseRequested;
    }

    private void OnAnalysisCloseRequested(object? sender, EventArgs e)
    {
        _viewModel.CloseAnalysis();
        Viewer.IsPicking = false;
        ApplyViewModelState();
        ShowChrome();
    }

    private void OnAnalysisSamplesChanged(object? sender, EventArgs e)
    {
        var pins = _viewModel.Analysis.Pins;
        Viewer.Samples = pins.Count == 0
            ? null
            : pins.Select(pin => new ViewerSample(
                pin.X,
                pin.Y,
                pin.Brush is ISolidColorBrush solid ? solid.Color : Colors.White,
                int.TryParse(pin.IndexText, out var index) ? index : 0)).ToArray();
    }

    private void OnViewerPickHover(object? sender, ViewerPickEventArgs e)
    {
        if (!_viewModel.Analysis.IsEyedropperActive ||
            !Viewer.TryGetImagePixel(e.Position, out var x, out var y) ||
            !_viewModel.TryGetDisplayedPixel(x, y, out var color, out var alpha))
        {
            return;
        }

        _viewModel.Analysis.HoverSample(x, y, color, alpha);
    }

    private void OnViewerPickRequested(object? sender, ViewerPickEventArgs e)
    {
        if (!_viewModel.Analysis.IsEyedropperActive ||
            !Viewer.TryGetImagePixel(e.Position, out var x, out var y) ||
            !_viewModel.TryGetDisplayedPixel(x, y, out var color, out var alpha))
        {
            return;
        }

        _viewModel.Analysis.PinSample(x, y, color, alpha);
    }

    private async Task ToggleDualAsync()
    {
        if (_dualMode)
        {
            await ExitDualAsync();
        }
        else
        {
            await EnterDualAsync();
        }
    }

    private async Task EnterDualAsync()
    {
        if (_dualMode)
        {
            return;
        }

        if (_viewModel.IsEditing)
        {
            await CloseEditAsync();
        }

        _viewModel.CloseAnalysis();
        Viewer.IsPicking = false;

        if (_dualView is null)
        {
            _dualView = new DualCanvasView(_services);
            _dualView.OpenRequested += OnDualOpenRequested;
            _dualView.ExitRequested += OnDualExitRequested;
            _dualView.RevealRequested += OnDualRevealRequested;
            _dualView.PointerPressed += OnDualPointerPressed;
            _dualView.SetValue(Panel.ZIndexProperty, 45);
            _root.Children.Add(_dualView);
        }

        _dualMode = true;
        _dualView.IsVisible = true;
        MinWidth = 1000;
        if (WindowState == WindowState.Normal && Width < 1000)
        {
            Width = 1000;
        }

        ApplyViewModelState();
        Title = "AirGlance — Compare";
        await _dualView.InitializeAsync(_viewModel.CurrentFilePath);
        _dualView.Focus();
    }

    private async Task ExitDualAsync()
    {
        if (!_dualMode)
        {
            return;
        }

        var path = _dualView?.ActivePath ?? _viewModel.CurrentFilePath;
        _dualMode = false;
        if (_dualView is not null)
        {
            _dualView.ResetOverlays();
            _dualView.IsVisible = false;
        }

        MinWidth = 720;
        ApplyViewModelState();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.OpenPathAsync(path);
        }

        Title = _viewModel.WindowTitle;
        Viewer.Focus();
    }

    private async void OnDualOpenRequested(object? sender, DualOpenRequest e) =>
        await OpenDualPaneAsync(e.PaneIndex, e.Folder);

    private async void OnDualExitRequested(object? sender, EventArgs e) =>
        await ExitDualAsync();

    private void OnDualRevealRequested(object? sender, DualRevealRequest e)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{e.Path}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", e.Path]);
            }
            else
            {
                Process.Start("xdg-open", Path.GetDirectoryName(e.Path) ?? e.Path);
            }
        }
        catch
        {
            // File managers differ; nothing useful to say beyond the failure.
        }
    }

    private async Task OpenDualAsync(bool folder) =>
        await OpenDualPaneAsync(_dualView?.ActiveIndex ?? 0, folder);

    private async Task OpenDualPaneAsync(int paneIndex, bool folder)
    {
        if (_dualView is null)
        {
            return;
        }

        string? path;
        if (folder)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = _services.Localization.T("L.OpenFolder"),
                    AllowMultiple = false
                });
            path = folders.FirstOrDefault()?.TryGetLocalPath();
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = _services.Localization.T("L.OpenFile"),
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Images")
                        {
                            Patterns = SupportedFormats.Extensions.Select(extension => "*" + extension).ToList()
                        }
                    ]
                });
            path = files.FirstOrDefault()?.TryGetLocalPath();
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            await _dualView.OpenPathAsync(paneIndex, path);
        }
    }

    private void OnDualPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            e.GetPosition(this).Y < 58)
        {
            BeginMoveDrag(e);
        }
    }

    private async Task CopyDualImageAsync()
    {
        var bitmap = _dualView?.ActiveBitmap;
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

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsSlideshowRunning)
        {
            _viewModel.StopSlideshow();
            e.Handled = true;
            return;
        }

        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        try
        {
            if (!control && e.Key == Key.D)
            {
                await ToggleDualAsync();
                e.Handled = true;
                return;
            }

            if (_dualMode && _dualView is not null)
            {
                if (e.Key == Key.Escape && _isFullscreen && !_dualView.HasOverlay)
                {
                    ExitFullscreen();
                    e.Handled = true;
                    return;
                }

                if (control && e.Key == Key.O)
                {
                    await OpenDualAsync(folder: shift);
                    e.Handled = true;
                    return;
                }

                if (control && e.Key == Key.C && !shift)
                {
                    await CopyDualImageAsync();
                    e.Handled = true;
                    return;
                }

                if (control && e.Key == Key.L && shift)
                {
                    ToggleLanguage();
                    e.Handled = true;
                    return;
                }

                if (e.Key is Key.F11 or Key.F)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.F1)
                {
                    ShowShortcuts();
                    e.Handled = true;
                    return;
                }

                if (_dualView.HandleKeyDown(e))
                {
                    e.Handled = true;
                    return;
                }

                e.Handled = true;
                return;
            }

            if (control)
            {
                switch (e.Key)
                {
                    case Key.O:
                        if (shift)
                        {
                            await OpenFolderAsync();
                        }
                        else
                        {
                            await OpenFileAsync();
                        }

                        break;
                    case Key.C:
                        if (shift)
                        {
                            await CopyPathAsync();
                        }
                        else
                        {
                            await CopyImageAsync();
                        }

                        break;
                    case Key.S when shift:
                        await SaveCopyAsync();
                        break;
                    case Key.E:
                        OnEditClick(this, new RoutedEventArgs());
                        break;
                    case Key.L when shift:
                        ToggleLanguage();
                        break;
                    default:
                        return;
                }

                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Left:
                    await _viewModel.PreviousAsync();
                    break;
                case Key.Right:
                    await _viewModel.NextAsync();
                    break;
                case Key.PageUp:
                    await _viewModel.PreviousAsync();
                    break;
                case Key.PageDown:
                case Key.Space:
                    await _viewModel.NextAsync();
                    break;
                case Key.Back:
                    await _viewModel.PreviousAsync();
                    break;
                case Key.OemPlus:
                case Key.Add:
                    Viewer.ZoomIn();
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    Viewer.ZoomOut();
                    break;
                case Key.D0:
                case Key.NumPad0:
                    Viewer.AnimateZoomMode(ViewerZoomMode.FitWindow);
                    break;
                case Key.D1:
                case Key.NumPad1:
                    Viewer.AnimateZoomMode(ViewerZoomMode.Actual);
                    break;
                case Key.D2:
                case Key.NumPad2:
                    Viewer.AnimateZoomMode(ViewerZoomMode.FitWidth);
                    break;
                case Key.R:
                    if (shift)
                    {
                        await _viewModel.RotateCounterClockwiseAsync();
                    }
                    else
                    {
                        await _viewModel.RotateClockwiseAsync();
                    }

                    break;
                case Key.H:
                    await _viewModel.FlipHorizontalAsync();
                    break;
                case Key.V:
                    await _viewModel.FlipVerticalAsync();
                    break;
                case Key.Enter when _cropMode:
                    OnCropBarDoneRequested(this, EventArgs.Empty);
                    break;
                case Key.F11:
                case Key.F:
                    ToggleFullscreen();
                    break;
                case Key.Escape:
                    if (_cropMode)
                    {
                        OnCropBarCancelRequested(this, EventArgs.Empty);
                        break;
                    }

                    if (_viewModel.IsEditing)
                    {
                        await CloseEditAsync();
                        break;
                    }

                    if (_isFullscreen)
                    {
                        ExitFullscreen();
                    }
                    else if (_viewModel.AnalysisVisible)
                    {
                        _viewModel.CloseAnalysis();
                        Viewer.IsPicking = false;
                        ApplyViewModelState();
                        ShowChrome();
                    }
                    else if (_viewModel.InfoVisible)
                    {
                        _viewModel.ToggleInfo();
                        ShowChrome();
                    }
                    else if (_viewModel.FilmstripVisible)
                    {
                        _viewModel.ToggleFilmstrip();
                        ShowChrome();
                    }
                    else
                    {
                        return;
                    }

                    break;
                case Key.S:
                    _viewModel.ToggleSlideshow();
                    break;
                case Key.I:
                    EnsurePanels();
                    if (!_viewModel.InfoVisible && _viewModel.FilmstripVisible)
                    {
                        _viewModel.ToggleFilmstrip();
                    }

                    _viewModel.ToggleInfo();
                    ApplyViewModelState();
                    ShowChrome();
                    break;
                case Key.C:
                    EnsurePanels();
                    if (!_viewModel.FilmstripVisible && _viewModel.InfoVisible)
                    {
                        _viewModel.ToggleInfo();
                    }

                    _viewModel.ToggleFilmstrip();
                    ApplyViewModelState();
                    ShowChrome();
                    break;
                case Key.F1:
                    ShowShortcuts();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }
        catch
        {
            // A shortcut failure must not take the window down.
        }
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

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = _services.Localization.T("L.OpenFile"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = SupportedFormats.Extensions.Select(extension => "*" + extension).ToList()
                    }
                ]
            });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.OpenPathAsync(path);
        }
    }

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = _services.Localization.T("L.OpenFolder"),
                AllowMultiple = false
            });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.OpenPathAsync(path);
        }
    }

    private async Task CopyImageAsync()
    {
        var current = _viewModel.CurrentBitmap;
        if (current is null)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.NoImage"));
            return;
        }

        var bitmap = _viewModel.IsTiled
            ? await _viewModel.GetClipboardBitmapAsync(CancellationToken.None)
            : current;
        if (bitmap is null)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.NoImage"));
            return;
        }

        var ownsBitmap = !ReferenceEquals(bitmap, current);
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            if (_viewModel.CurrentEdit.IsIdentity &&
                _viewModel.CurrentFilePath is { } path &&
                TryGetMimeType(path) is { } mime)
            {
                var bytes = await Task.Run(() => File.ReadAllBytes(path));
                using var transfer = new DataTransfer();
                var bitmapItem = new DataTransferItem();
                bitmapItem.SetBitmap(bitmap);
                transfer.Add(bitmapItem);

                var format = DataFormat.CreateBytesPlatformFormat(mime);
                transfer.Add(DataTransferItem.Create(format, bytes));

                await clipboard.SetDataAsync(transfer);
            }
            else
            {
                await clipboard.SetBitmapAsync(bitmap);
            }

            _viewModel.ShowMessage(_services.Localization.T("L.CopiedImage"));
        }
        catch
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetBitmapAsync(bitmap);
                    _viewModel.ShowMessage(_services.Localization.T("L.CopiedImage"));
                    return;
                }
            }
            catch
            {
                // Fall through to the failure message.
            }

            _viewModel.ShowMessage(_services.Localization.T("L.CopyFailed"));
        }
        finally
        {
            if (ownsBitmap)
            {
                bitmap.Dispose();
            }
        }
    }

    private async Task CopyPathAsync()
    {
        if (_viewModel.CurrentFilePath is not { } path)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.NoImage"));
            return;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(path);
                _viewModel.ShowMessage(_services.Localization.T("L.CopiedPath"));
            }
        }
        catch
        {
            _viewModel.ShowMessage(_services.Localization.T("L.CopyFailed"));
        }
    }

    private async Task SaveCopyAsync()
    {
        if (_viewModel.CurrentFilePath is not { } path)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.NoImage"));
            return;
        }

        var viewModel = new SaveCopyViewModel(
            path,
            _services.Localization,
            (format, cancellationToken) => _viewModel.EncodeCurrentAsync(format, cancellationToken));

        var dialog = new SaveCopyDialog(viewModel);
        var savedPath = await dialog.ShowDialog<string?>(this);

        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            _viewModel.ShowMessage(_services.Localization.Format("L.SaveSucceeded", savedPath));
        }
    }

    private void ToggleLanguage()
    {
        _services.SetLanguage(
            _services.Localization.IsChinese
                ? LocalizationService.English
                : LocalizationService.ChineseSimplified);
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e) => await OpenFileAsync();

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e) => await OpenFolderAsync();

    private async void OnCopyImageClick(object? sender, RoutedEventArgs e) => await CopyImageAsync();

    private async void OnCopyPathClick(object? sender, RoutedEventArgs e) => await CopyPathAsync();

    private void OnShowInFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFilePath is not { } path)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path]);
            }
            else
            {
                Process.Start("xdg-open", Path.GetDirectoryName(path) ?? path);
            }
        }
        catch
        {
            // File managers differ; nothing useful to say beyond the failure.
        }
    }

    private void OnOpenWithClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFilePath is not { } path)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
            if (OperatingSystem.IsWindows())
            {
                startInfo.Verb = "openas";
            }

            Process.Start(startInfo);
        }
        catch
        {
            // The shell refused the request.
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_services);
        await dialog.ShowDialog(this);
    }

    private async void OnShortcutsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutsWindow(_services.Localization);
        await dialog.ShowDialog(this);
    }

    private void ShowShortcuts()
    {
        var dialog = new ShortcutsWindow(_services.Localization);
        _ = dialog.ShowDialog(this);
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e) =>
        AppServices.OpenExternalUrl("https://github.com/RyzenWang/AirGlance");

    private void OnZoomPresetClick(object? sender, RoutedEventArgs e) => Viewer.CycleZoomPreset();

    private async void OnOpenImageClick(object? sender, RoutedEventArgs e) =>
        await OpenFileAsync();

    private async void OnPreviousClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.PreviousAsync();

    private async void OnNextClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.NextAsync();

    private void OnInfoToggleClick(object? sender, RoutedEventArgs e)
    {
        EnsurePanels();
        if (!_viewModel.InfoVisible && _viewModel.FilmstripVisible)
        {
            _viewModel.ToggleFilmstrip();
        }

        _viewModel.ToggleInfo();
        ApplyViewModelState();
        ShowChrome();
    }

    private void OnFilmstripToggleClick(object? sender, RoutedEventArgs e)
    {
        EnsurePanels();
        if (!_viewModel.FilmstripVisible && _viewModel.InfoVisible)
        {
            _viewModel.ToggleInfo();
        }

        _viewModel.ToggleFilmstrip();
        ApplyViewModelState();
        ShowChrome();
    }

    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (_dualMode)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.EditDualUnavailable"));
            return;
        }

        if (_viewModel.IsEditing)
        {
            await CloseEditAsync();
            return;
        }

        if (!_viewModel.HasImage)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.EditNoImage"));
            return;
        }

        if (!_viewModel.BeginEdit())
        {
            return;
        }


        EnsurePanels();
        if (_viewModel.InfoVisible)
        {
            _viewModel.ToggleInfo();
        }

        _cropMode = false;
        _cropAspect = "free";
        _cropBeforeEnter = CropRegion.Full;
        _cropAspectBeforeEnter = "free";
        CropControl.Reset();
        CropControl.AspectRatio = null;
        CropControl.IsActive = false;
        CropBar.SetActiveRatio("free");
        _viewModel.SetCrop(CropRegion.Full);
        UpdateCropUi();
        ApplyViewModelState();
        ShowChrome();
    }

    private async void OnEditCloseRequested(object? sender, EventArgs e) =>
        await CloseEditAsync();

    private async void OnEditResetRequested(object? sender, EventArgs e)
    {
        await _viewModel.ResetEditAsync();
        CropControl.Reset();
        CropControl.AspectRatio = null;
        _cropAspect = "free";
        _viewModel.SetCrop(CropRegion.Full);
        UpdateCropUi();
        ApplyViewModelState();
    }

    private async void OnEditTransformRequested(
        object? sender,
        EditPanelView.EditAction action)
    {
        switch (action)
        {
            case EditPanelView.EditAction.Left:
                await _viewModel.RotateCounterClockwiseAsync();
                break;
            case EditPanelView.EditAction.Right:
                await _viewModel.RotateClockwiseAsync();
                break;
            case EditPanelView.EditAction.FlipH:
                await _viewModel.FlipHorizontalAsync();
                break;
            case EditPanelView.EditAction.FlipV:
                await _viewModel.FlipVerticalAsync();
                break;
        }

        CropControl.Reset();
        CropControl.AspectRatio = null;
        _cropAspect = "free";
        _viewModel.SetCrop(CropRegion.Full);
        UpdateCropUi();
        ApplyViewModelState();
    }

    private void OnEditCropRequested(object? sender, EventArgs e) => EnterCropMode();

    private void EnterCropMode()
    {
        if (!_viewModel.IsEditing || _cropMode)
        {
            return;
        }

        _cropBeforeEnter = _viewModel.Crop;
        _cropAspectBeforeEnter = _cropAspect;
        _cropMode = true;
        CropControl.RefreshTheme();
        CropControl.SetCrop(_cropBeforeEnter);
        CropControl.IsActive = true;
        CropBar.SetActiveRatio(_cropAspect);
        UpdateCropUi();
        ApplyViewModelState();
        ShowChrome();
    }

    private void OnCropBarResetRequested(object? sender, EventArgs e)
    {
        CropControl.Reset();
        _viewModel.SetCrop(CropRegion.Full);
        UpdateCropUi();
    }

    private void OnCropBarCancelRequested(object? sender, EventArgs e)
    {
        _cropAspect = _cropAspectBeforeEnter;
        CropControl.AspectRatio = AspectFor(_cropAspect);
        _viewModel.SetCrop(_cropBeforeEnter);
        CropControl.SetCrop(_cropBeforeEnter);
        CropBar.SetActiveRatio(_cropAspect);
        _cropMode = false;
        CropControl.IsActive = false;
        UpdateCropUi();
        ApplyViewModelState();
        ShowChrome();
    }

    private void OnCropBarDoneRequested(object? sender, EventArgs e)
    {
        // Keep crop mode active while the export dialog is open. If the user
        // cancels the dialog, the crop canvas and its current region are already
        // exactly where they were; only a completed export leaves crop mode.
        OnEditExportRequested(this, EventArgs.Empty);
    }

    private void OnEditCropAspectRequested(object? sender, string ratio)
    {
        double? aspect = ratio switch
        {
            "original" when Viewer.SourcePixelWidth > 0 && Viewer.SourcePixelHeight > 0 =>
                (double)Viewer.SourcePixelWidth / Viewer.SourcePixelHeight,
            "1:1" => 1,
            "4:3" => 4.0 / 3,
            "16:9" => 16.0 / 9,
            _ => null
        };

        if (!_cropMode)
        {
            EnterCropMode();
        }

        _cropAspect = ratio;
        CropControl.AspectRatio = aspect;
        CropControl.IsActive = true;
        CropBar.SetActiveRatio(ratio);
        _viewModel.SetCrop(CropControl.Crop);
        UpdateCropUi();
    }

    private void OnCropChanged(object? sender, EventArgs e)
    {
        _viewModel.SetCrop(CropControl.Crop);
        UpdateCropUi();
    }

    private void UpdateCropUi()
    {
        if (!_viewModel.IsEditing)
        {
            return;
        }

        var crop = CropControl.Crop;
        var width = Math.Max(1, (int)Math.Round(Viewer.SourcePixelWidth * crop.Width));
        var height = Math.Max(1, (int)Math.Round(Viewer.SourcePixelHeight * crop.Height));
        var label = _cropAspect switch
        {
            "original" => _services.Localization.T("L.EditOriginalRatio"),
            "1:1" => "1:1",
            "4:3" => "4:3",
            "16:9" => "16:9",
            _ => _services.Localization.T("L.EditFree")
        };
        var text = _services.Localization.Format(
            "L.EditCropSizeFormat",
            width,
            height,
            label);
        EditPanel.SetCropSize(text);
        CropBar.SetSize($"{width} × {height}");
    }

    private double? AspectFor(string ratio) => ratio switch
    {
        "original" when Viewer.SourcePixelWidth > 0 && Viewer.SourcePixelHeight > 0 =>
            (double)Viewer.SourcePixelWidth / Viewer.SourcePixelHeight,
        "1:1" => 1,
        "4:3" => 4.0 / 3,
        "16:9" => 16.0 / 9,
        _ => null
    };

    private async void OnEditExportRequested(object? sender, EventArgs e)
    {
        if (!_viewModel.IsEditing || _viewModel.CurrentFilePath is not { } path)
        {
            return;
        }

        var defaults = new EditExportOptions(
            ExportFormat.Jpeg,
            90,
            EditResizeMode.Original,
            4096,
            KeepMetadata: true);
        var crop = _viewModel.Crop;
        var cropWidth = Math.Max(1, (int)Math.Round(Viewer.SourcePixelWidth * crop.Width));
        var cropHeight = Math.Max(1, (int)Math.Round(Viewer.SourcePixelHeight * crop.Height));
        var croppedPreview = CreateCroppedPreviewBitmap(crop);
        var dialog = new EditExportDialog(
            _services,
            path,
            croppedPreview,
            croppedPreview is null ? crop : CropRegion.Full,
            cropWidth,
            cropHeight,
            defaults,
            (options, target) => _viewModel.ExportEditedAsync(target, options, CancellationToken.None));

        _exportScrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(148, 10, 8, 6)),
            IsHitTestVisible = true
        };
        _exportScrim.SetValue(Panel.ZIndexProperty, 65);
        _root.Children.Add(_exportScrim);
        var previousEffect = _root.Effect;
        _root.Effect = new BlurEffect { Radius = 7 };
        string? result;
        try
        {
            result = await dialog.ShowDialog<string?>(this);
        }
        finally
        {
            _root.Effect = previousEffect;
            _root.Children.Remove(_exportScrim);
            _exportScrim = null;
            croppedPreview?.Dispose();
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return;
        }

        await _viewModel.FinishEditAsync();
        _cropMode = false;
        CropControl.IsActive = false;
        ApplyViewModelState();
        ShowChrome();
    }

    private WriteableBitmap? CreateCroppedPreviewBitmap(CropRegion crop)
    {
        if (_viewModel.CurrentBitmap is not { } source)
        {
            return null;
        }

        var sourceWidth = source.PixelSize.Width;
        var sourceHeight = source.PixelSize.Height;
        var (x, y, width, height) = crop.Clamp().ToPixels(sourceWidth, sourceHeight);
        var target = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        try
        {
            using var sourceBuffer = source.Lock();
            using var targetBuffer = target.Lock();
            var rowBytes = width * 4;
            var row = new byte[rowBytes];
            for (var rowIndex = 0; rowIndex < height; rowIndex++)
            {
                Marshal.Copy(
                    sourceBuffer.Address + (y + rowIndex) * sourceBuffer.RowBytes + x * 4,
                    row,
                    0,
                    rowBytes);
                Marshal.Copy(row, 0, targetBuffer.Address + rowIndex * targetBuffer.RowBytes, rowBytes);
            }

            return target;
        }
        catch
        {
            target.Dispose();
            return null;
        }
    }

    private async Task CloseEditAsync()
    {
        await _viewModel.CancelEditAsync();
        _cropMode = false;
        CropControl.IsActive = false;
        ApplyViewModelState();
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e) =>
        ToggleFullscreen();

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnThemeClick(object? sender, RoutedEventArgs e) =>
        _services.SetTheme(_services.Theme.IsDark ? ThemeService.LightMode : ThemeService.DarkMode);

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            ExitFullscreen();
        }
        else
        {
            EnterFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        _stateBeforeFullscreen = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState;
        _isFullscreen = true;
        WindowState = WindowState.FullScreen;
        _viewModel.SetFullscreen(true);
        _chromeVisible = false;
        RefreshChromeVisibility();
        _chromeTimer.Stop();
        _chromeTimer.Start();
        _cursorTimer.Start();
    }

    private void ExitFullscreen()
    {
        _isFullscreen = false;
        WindowState = _stateBeforeFullscreen == WindowState.FullScreen
            ? WindowState.Normal
            : _stateBeforeFullscreen;
        _viewModel.SetFullscreen(false);
        _chromeVisible = true;
        RefreshChromeVisibility();
        _cursorTimer.Stop();
        _chromeTimer.Stop();
        _chromeTimer.Start();
        _cursorHidden = false;
        Viewer.ShowCursor();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_cursorHidden)
        {
            _cursorHidden = false;
            Viewer.ShowCursor();
        }

        if (_isFullscreen)
        {
            _cursorTimer.Stop();
            _cursorTimer.Start();
        }

        ShowChrome();
    }

    private void OnCursorTimerTick(object? sender, EventArgs e)
    {
        _cursorTimer.Stop();
        if (!_isFullscreen)
        {
            return;
        }

        _cursorHidden = true;
        Viewer.HideCursor();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var path = GetFirstDroppedPath(e);
        var supported = path is not null &&
                        (Directory.Exists(path) || SupportedFormats.IsSupported(path));

        _viewModel.IsDragOver = supported;
        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsDragOver = false;
        e.Handled = true;
    }

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        _contextMenu = null;
        _analysisMenu = null;
        _eyedropperMenuItem = null;
        CanvasBorder.ContextMenu = null;
        ApplyLanguage();
    }

    private void OnCropCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_viewModel.IsEditing || !_cropMode)
        {
            return;
        }

        if (!e.GetCurrentPoint(CanvasBorder).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (CropControl.BeginDrag(e.GetPosition(CropControl)))
        {
            _cropDragging = true;
            e.Pointer.Capture(CanvasBorder);
            e.Handled = true;
        }
    }

    private void OnCropCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_cropDragging)
        {
            return;
        }

        CropControl.DragTo(e.GetPosition(CropControl));
        e.Handled = true;
    }

    private void OnCropCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_cropDragging)
        {
            return;
        }

        _cropDragging = false;
        CropControl.EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CanvasBorder).Properties.IsRightButtonPressed)
        {
            return;
        }

        _contextMenu ??= BuildContextMenu();
        UpdateAnalysisMenuState();
        _contextMenu.Open(CanvasBorder);
        e.Handled = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var localization = _services.Localization;
        var menu = new ContextMenu();

        void Add(string key, EventHandler<RoutedEventArgs> handler)
        {
            var item = new MenuItem { Header = localization.T(key) };
            item.Click += handler;
            menu.Items.Add(item);
        }

        Add("L.OpenFile", OnOpenFileClick);
        Add("L.OpenFolder", OnOpenFolderClick);
        menu.Items.Add(new Separator());
        Add("L.CopyImage", OnCopyImageClick);
        Add("L.CopyPath", OnCopyPathClick);
        Add("L.ShowInFolder", OnShowInFolderClick);
        Add("L.OpenWith", OnOpenWithClick);
        menu.Items.Add(new Separator());

        _analysisMenu = new MenuItem { Header = localization.T("L.Analyze") };
        AddAnalysisItem(_analysisMenu, "L.AnalysisHistogramMenu", AnalysisTab.Histogram);
        AddAnalysisItem(_analysisMenu, "L.AnalysisColorMenu", AnalysisTab.Color);
        AddAnalysisItem(_analysisMenu, "L.AnalysisQualityMenu", AnalysisTab.Quality);
        AddAnalysisItem(_analysisMenu, "L.AnalysisDataMenu", AnalysisTab.Data);
        _analysisMenu.Items.Add(new Separator());
        _eyedropperMenuItem = new MenuItem
        {
            Header = localization.T("L.AnalysisEyedropper"),
            ToggleType = MenuItemToggleType.CheckBox
        };
        _eyedropperMenuItem.Click += OnEyedropperClick;
        _analysisMenu.Items.Add(_eyedropperMenuItem);
        menu.Items.Add(_analysisMenu);
        menu.Items.Add(new Separator());

        Add("L.Settings", OnSettingsClick);
        Add("L.Shortcuts", OnShortcutsClick);

        CanvasBorder.ContextMenu = menu;
        return menu;
    }

    private void AddAnalysisItem(MenuItem parent, string key, AnalysisTab tab)
    {
        var item = new MenuItem { Header = _services.Localization.T(key) };
        item.Click += (_, _) => OpenAnalysisTab(tab);
        parent.Items.Add(item);
    }

    private void OpenAnalysisTab(AnalysisTab tab)
    {
        if (_dualMode || _viewModel.IsEditing || _isFullscreen || _viewModel.IsLoading || !_viewModel.HasImage)
        {
            return;
        }

        EnsurePanels();
        EnsureAnalysisPanel();
        _viewModel.OpenAnalysis(tab);
        ApplyViewModelState();
        ShowChrome();
    }

    private void OnEyedropperClick(object? sender, RoutedEventArgs e)
    {
        var active = _eyedropperMenuItem?.IsChecked == true;
        if (active && (_dualMode || _viewModel.IsEditing || _isFullscreen || !_viewModel.HasImage))
        {
            if (_eyedropperMenuItem is not null)
            {
                _eyedropperMenuItem.IsChecked = false;
            }

            return;
        }

        _viewModel.Analysis.SetEyedropper(active);
        Viewer.IsPicking = active;
        if (active)
        {
            OpenAnalysisTab(AnalysisTab.Color);
        }

        ApplyViewModelState();
    }

    private void UpdateAnalysisMenuState()
    {
        if (_analysisMenu is not null)
        {
            _analysisMenu.IsEnabled =
                _viewModel.HasImage &&
                !_viewModel.IsEditing &&
                !_viewModel.IsLoading &&
                !_viewModel.IsFullscreen &&
                !_dualMode;
        }

        if (_eyedropperMenuItem is not null)
        {
            _eyedropperMenuItem.IsChecked = _viewModel.Analysis.IsEyedropperActive;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
        var path = GetFirstDroppedPath(e);
        if (path is not null)
        {
            await _viewModel.OpenDroppedPathAsync(path);
            EnsurePanels();
        }

        e.Handled = true;
    }

    private static string? GetFirstDroppedPath(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return null;
        }

        return files
            .Select(file => file.TryGetLocalPath())
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private static string? TryGetMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            _ => null
        };

    private void UpdateMaximizeIcon()
    {
        var key = WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize";
        if (this.FindResource(key) is Geometry geometry)
        {
            MaximizeIcon.Data = geometry;
        }
    }

    private async Task ShowCommandLineMessageAsync()
    {
        var localization = _services.Localization;
        string title;
        string body;

        if (_options.ShowHelp)
        {
            title = localization.T("L.CommandLineTitle");
            body = localization.T("L.CommandLineHelpBody");
        }
        else
        {
            title = localization.Format("L.CommandLineVersionFormat", AppServices.Version);
            body = string.Empty;
        }

        var dialog = new MessageWindow(title, body);
        await dialog.ShowDialog(this);
    }

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

