using System.ComponentModel;
using System.Diagnostics;
using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
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
    private ContextMenu? _contextMenu;
    private FilmstripView? _filmstripView;
    private InfoPanelView? _infoPanel;
    private bool _panelsCreated;
    private bool _isFullscreen;
    private bool _initialZoomApplied;
    private bool _cursorHidden;
    private bool _chromeVisible = true;

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
    private Border FilmstripHost = null!;
    private ContentControl InfoContentHost = null!;
    private Border EmptyCard = null!;
    private Border LoadingChip = null!;
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
    private Button InfoToggle = null!;
    private Button FilmstripToggle = null!;
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

        var root = new Grid();

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

        LoadingChip = new Border();
        LoadingChip.Classes.Add("loading-chip");
        LoadingText = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
        LoadingChip.Child = LoadingText;
        canvasGrid.Children.Add(LoadingChip);

        CanvasBorder.Child = canvasGrid;
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
        var fileRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        FileNameText = new TextBlock();
        FileNameText.Classes.Add("chrome-file");
        FileNameText.MaxWidth = 240;
        fileRow.Children.Add(FileNameText);
        fileRow.Children.Add(new Border
        {
            Width = 1,
            Height = 16,
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF")),
            VerticalAlignment = VerticalAlignment.Center
        });
        FileMetaText = new TextBlock();
        FileMetaText.Classes.Add("chrome-meta");
        fileRow.Children.Add(FileMetaText);
        FilePositionText = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#F4A593")) };
        FilePositionText.Classes.Add("chrome-meta");
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
        Grid.SetColumn(WindowChip, 2);
        topGrid.Children.Add(WindowChip);

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
        FitButton = CreateToolButton("IconFit", "L.FitWindow", (_, _) => Viewer.SetZoomMode(ViewerZoomMode.FitWindow));
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
        ActualButton.Click += (_, _) => Viewer.SetZoomMode(ViewerZoomMode.Actual);
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

        SlideshowButton = CreateToolButton("IconPlay", "L.Slideshow", (_, _) => _viewModel.ToggleSlideshow());
        dockRow.Children.Add(SlideshowButton);

        dockRow.Children.Add(CreateDockDivider());
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
        ApplyTool(InfoToggle, "L.InfoPanel");
        ApplyTool(FilmstripToggle, "L.Filmstrip");
        ApplyTool(SlideshowButton, "L.Slideshow");
        ApplyTool(FullscreenButton, "L.Fullscreen");
        ApplyTool(SettingsButton, "L.Settings");
        ApplyTool(PrevButton, "L.PreviousImage");
        ApplyTool(NextButton, "L.NextImage");

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

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeButton();

    private void UpdateThemeButton()
    {
        var dark = _services.Theme.IsDark;
        if (ThemeButton.Content is IconPath icon)
        {
            icon.Data = FindGeometry(dark ? "IconMoon" : "IconTheme");
        }

        ApplyTool(ThemeButton, dark ? "L.SwitchToLight" : "L.SwitchToDark");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        ApplyViewModelState();

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
        InfoHost.IsVisible = _viewModel.InfoShown;
        InfoHost.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 82, 22, 150)
            : new Thickness(0, 82, 22, 92);
        FilmstripHost.IsVisible = _viewModel.FilmstripShown;
        Dock.IsVisible = !_viewModel.OverlayVisible;
        Dock.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 0, 0, 150)
            : new Thickness(0, 0, 0, 22);

        var showNavigation = _viewModel.HasImage && count > 1 && !_viewModel.OverlayVisible;
        PrevButton.IsVisible = showNavigation;
        NextButton.IsVisible = showNavigation;

        InfoToggle.Classes.Set("active", _viewModel.InfoShown);
        FilmstripToggle.Classes.Set("active", _viewModel.FilmstripShown);
        FullscreenButton.Classes.Set("active", _viewModel.IsFullscreen);
        TopBar.IsVisible = !_viewModel.IsFullscreen;

        TransientMessageText.Text = _viewModel.TransientMessage ?? string.Empty;
        ToastBorder.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.TransientMessage);
        DragOverlay.IsVisible = _viewModel.IsDragOver;

        CanvasBorder.Padding = _viewModel.IsFullscreen
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
        TopBar.IsVisible = !_viewModel.IsFullscreen;
        TopBar.Opacity = topVisible ? 1 : 0;
        TopBar.IsHitTestVisible = topVisible;

        var controlsVisible = _chromeVisible && !empty;
        Dock.Opacity = controlsVisible ? 1 : 0;
        Dock.IsHitTestVisible = controlsVisible;
        var navVisible = controlsVisible && !_viewModel.IsFullscreen;
        PrevHost.Opacity = navVisible ? 1 : 0;
        NextHost.Opacity = navVisible ? 1 : 0;
        PrevHost.IsHitTestVisible = navVisible;
        NextHost.IsHitTestVisible = navVisible;

        // In fullscreen the hint appears once the dock has faded out, so the two
        // never share the bottom-right corner on a narrow screen.
        FullscreenHint.IsVisible = _viewModel.IsFullscreen && !_chromeVisible;
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
    }

    private void OnViewerViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        _viewModel.SetViewport(e.PixelWidth, e.PixelHeight);
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
                    Viewer.SetZoomMode(ViewerZoomMode.FitWindow);
                    break;
                case Key.D1:
                case Key.NumPad1:
                    Viewer.SetZoomMode(ViewerZoomMode.Actual);
                    break;
                case Key.D2:
                case Key.NumPad2:
                    Viewer.SetZoomMode(ViewerZoomMode.FitWidth);
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
                case Key.F11:
                case Key.F:
                    ToggleFullscreen();
                    break;
                case Key.Escape:
                    if (_isFullscreen)
                    {
                        ExitFullscreen();
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
        CanvasBorder.ContextMenu = null;
        ApplyLanguage();
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CanvasBorder).Properties.IsRightButtonPressed)
        {
            return;
        }

        _contextMenu ??= BuildContextMenu();
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
        Add("L.Settings", OnSettingsClick);
        Add("L.Shortcuts", OnShortcutsClick);

        CanvasBorder.ContextMenu = menu;
        return menu;
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
        _viewModel.Dispose();
    }
}

