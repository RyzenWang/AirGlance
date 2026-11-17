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
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace AirGlance.App.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly CommandLineOptions _options;
    private readonly BenchmarkRecorder? _benchmark;
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _cursorTimer;
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

    private Border TitleBar = null!;
    private Border CanvasBorder = null!;
    private Border InfoHost = null!;
    private Border FilmstripHost = null!;
    private Border StatusBar = null!;
    private Border LoadingChip = null!;
    private Border DragOverlay = null!;
    private StackPanel CaptionButtons = null!;
    private StackPanel OverlayPanel = null!;
    private StackPanel StatusLeft = null!;
    private TextBlock FileNameText = null!;
    private TextBlock OverlayTitle = null!;
    private TextBlock OverlayMessage = null!;
    private TextBlock OverlayHint = null!;
    private TextBlock LoadingText = null!;
    private TextBlock DropHintText = null!;
    private TextBlock StatusDimensions = null!;
    private TextBlock StatusPosition = null!;
    private TextBlock TransientMessageText = null!;
    private Button ZoomButton = null!;
    private Button GitHubButton = null!;
    private Button MinimizeButton = null!;
    private Button MaximizeButton = null!;
    private Button CloseButton = null!;
    private ShapePath MaximizeIcon = null!;

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
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ImagePresented += OnImagePresented;
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

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto")
        };

        // Title row.
        TitleBar = new Border { Height = 44 };
        TitleBar.Classes.Add("titlebar");
        TitleBar.PointerPressed += OnTitleBarPointerPressed;

        var titleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var brandMark = new Border();
        brandMark.Classes.Add("brandmark");
        var brandGlyph = new ShapePath { Data = FindGeometry("IconBrand") };
        brandGlyph.Classes.Add("brand-glyph");
        brandMark.Child = brandGlyph;
        brand.Children.Add(brandMark);
        var brandName = new TextBlock { Text = "AirGlance" };
        brandName.Classes.Add("brand-name");
        brand.Children.Add(brandName);
        titleGrid.Children.Add(brand);

        FileNameText = new TextBlock { Margin = new Thickness(16, 0) };
        FileNameText.Classes.Add("filename");
        Grid.SetColumn(FileNameText, 1);
        titleGrid.Children.Add(FileNameText);

        CaptionButtons = new StackPanel { Orientation = Orientation.Horizontal };
        MinimizeButton = CreateCaptionButton("IconMinimize", "L.Minimize", OnMinimizeClick);
        MaximizeButton = CreateCaptionButton("IconMaximize", "L.Maximize", OnMaximizeClick);
        MaximizeIcon = (ShapePath)MaximizeButton.Content!;
        CloseButton = CreateCaptionButton("IconClose", "L.Close", OnCloseClick, close: true);
        CaptionButtons.Children.Add(MinimizeButton);
        CaptionButtons.Children.Add(MaximizeButton);
        CaptionButtons.Children.Add(CloseButton);
        Grid.SetColumn(CaptionButtons, 2);
        titleGrid.Children.Add(CaptionButtons);

        TitleBar.Child = titleGrid;
        Grid.SetRow(TitleBar, 0);
        root.Children.Add(TitleBar);

        // Content row.
        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        CanvasBorder = new Border { ClipToBounds = true };
        CanvasBorder.Classes.Add("canvas");

        var canvasGrid = new Grid();
        Viewer.FirstFramePresented += OnFirstFramePresented;
        Viewer.ZoomChanged += OnViewerZoomChanged;
        Viewer.ViewportChanged += OnViewerViewportChanged;
        canvasGrid.Children.Add(Viewer);

        OverlayPanel = new StackPanel
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 480,
            Spacing = 8
        };
        OverlayTitle = new TextBlock();
        OverlayTitle.Classes.Add("overlay-title");
        OverlayMessage = new TextBlock();
        OverlayMessage.Classes.Add("overlay-message");
        OverlayHint = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        OverlayHint.Classes.Add("overlay-hint");
        OverlayPanel.Children.Add(OverlayTitle);
        OverlayPanel.Children.Add(OverlayMessage);
        OverlayPanel.Children.Add(OverlayHint);
        canvasGrid.Children.Add(OverlayPanel);

        LoadingChip = new Border();
        LoadingChip.Classes.Add("loading-chip");
        LoadingText = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
        LoadingChip.Child = LoadingText;
        canvasGrid.Children.Add(LoadingChip);

        CanvasBorder.Child = canvasGrid;
        contentGrid.Children.Add(CanvasBorder);

        InfoHost = new Border
        {
            Width = 300,
            Margin = new Thickness(0, 12, 12, 12)
        };
        InfoHost.Classes.Add("panel");
        Grid.SetColumn(InfoHost, 1);
        contentGrid.Children.Add(InfoHost);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        // Filmstrip host.
        FilmstripHost = new Border { Height = 108 };
        FilmstripHost.Classes.Add("filmstrip-host");
        Grid.SetRow(FilmstripHost, 2);
        root.Children.Add(FilmstripHost);

        // Status row.
        StatusBar = new Border { Height = 28 };
        StatusBar.Classes.Add("statusbar");
        var statusGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 0)
        };

        StatusLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        StatusDimensions = new TextBlock();
        StatusDimensions.Classes.Add("mono");
        StatusDimensions.Classes.Add("status-text");
        StatusLeft.Children.Add(StatusDimensions);
        StatusLeft.Children.Add(CreateStatusDot());

        ZoomButton = new Button { Content = string.Empty };
        ZoomButton.Classes.Add("link");
        ZoomButton.Classes.Add("zoom-link");
        ZoomButton.Click += OnZoomPresetClick;
        StatusLeft.Children.Add(ZoomButton);
        StatusLeft.Children.Add(CreateStatusDot());

        StatusPosition = new TextBlock();
        StatusPosition.Classes.Add("mono");
        StatusPosition.Classes.Add("status-text");
        StatusLeft.Children.Add(StatusPosition);
        statusGrid.Children.Add(StatusLeft);

        TransientMessageText = new TextBlock { Margin = new Thickness(12, 0) };
        TransientMessageText.Classes.Add("transient");
        Grid.SetColumn(TransientMessageText, 1);
        statusGrid.Children.Add(TransientMessageText);

        GitHubButton = new Button { Content = "GitHub", FontSize = 12 };
        GitHubButton.Classes.Add("link");
        GitHubButton.Click += OnGitHubClick;
        AutomationProperties.SetName(GitHubButton, "GitHub");
        Grid.SetColumn(GitHubButton, 2);
        statusGrid.Children.Add(GitHubButton);

        StatusBar.Child = statusGrid;
        Grid.SetRow(StatusBar, 3);
        root.Children.Add(StatusBar);

        // Drag and drop overlay.
        DragOverlay = new Border { IsVisible = false };
        DragOverlay.Classes.Add("dragover");
        Grid.SetRowSpan(DragOverlay, 4);
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

        Content = root;
    }

    private Button CreateCaptionButton(
        string iconResource,
        string automationKey,
        EventHandler<RoutedEventArgs> clickHandler,
        bool close = false)
    {
        var button = new Button
        {
            Content = new ShapePath { Data = FindGeometry(iconResource) }
        };
        button.Classes.Add("caption");
        if (close)
        {
            button.Classes.Add("close");
        }

        button.Click += clickHandler;
        button.Tag = automationKey;
        return button;
    }

    private TextBlock CreateStatusDot()
    {
        var dot = new TextBlock { Text = "\u00B7" };
        dot.Classes.Add("status-dot");
        return dot;
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
        ToolTip.SetTip(ZoomButton, _services.Localization.T("L.ZoomLevel"));
        AutomationProperties.SetName(ZoomButton, _services.Localization.T("L.ZoomLevel"));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        ApplyViewModelState();

    private void ApplyViewModelState()
    {
        Title = _viewModel.WindowTitle;
        FileNameText.Text = _viewModel.TitleFileName;
        TitleBar.IsVisible = _viewModel.ShowTitleBar;
        OverlayPanel.IsVisible = _viewModel.OverlayVisible;
        OverlayTitle.Text = _viewModel.OverlayTitle;
        OverlayMessage.Text = _viewModel.OverlayMessage;
        OverlayHint.Text = _viewModel.OverlayHint;
        LoadingChip.IsVisible = _viewModel.IsLoading;
        InfoHost.IsVisible = _viewModel.InfoShown;
        FilmstripHost.IsVisible = _viewModel.FilmstripShown;
        StatusBar.IsVisible = _viewModel.IsChromeVisible;
        StatusLeft.IsVisible = _viewModel.HasImage;
        StatusDimensions.Text = _viewModel.StatusDimensions;
        ZoomButton.Content = _viewModel.ZoomText;
        StatusPosition.Text = _viewModel.StatusPosition;
        TransientMessageText.Text = _viewModel.TransientMessage ?? string.Empty;
        DragOverlay.IsVisible = _viewModel.IsDragOver;

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

    private void OnImagePresented(object? sender, ImagePresentedEventArgs e)
    {
        if (e.Bitmap is null)
        {
            Viewer.ClearImage();
            return;
        }

        Viewer.SetImage(
            e.Bitmap,
            e.SourceWidth,
            e.SourceHeight,
            e.ImageScale,
            e.ResetView,
            e.Animate);

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
        InfoHost.Child = _infoPanel;
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
                    if (Viewer.CanPanHorizontally)
                    {
                        Viewer.PanBy(80, 0);
                    }
                    else
                    {
                        await _viewModel.PreviousAsync();
                    }

                    break;
                case Key.Right:
                    if (Viewer.CanPanHorizontally)
                    {
                        Viewer.PanBy(-80, 0);
                    }
                    else
                    {
                        await _viewModel.NextAsync();
                    }

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
                    ToggleFullscreen();
                    break;
                case Key.Escape:
                    if (_isFullscreen)
                    {
                        ExitFullscreen();
                    }
                    else
                    {
                        Close();
                    }

                    break;
                case Key.S:
                    _viewModel.ToggleSlideshow();
                    break;
                case Key.I:
                    EnsurePanels();
                    _viewModel.ToggleInfo();
                    break;
                case Key.C:
                    EnsurePanels();
                    _viewModel.ToggleFilmstrip();
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
        var bitmap = _viewModel.CurrentBitmap;
        if (bitmap is null)
        {
            _viewModel.ShowMessage(_services.Localization.T("L.NoImage"));
            return;
        }

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

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

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
        _cursorTimer.Start();
    }

    private void ExitFullscreen()
    {
        _isFullscreen = false;
        WindowState = _stateBeforeFullscreen == WindowState.FullScreen
            ? WindowState.Normal
            : _stateBeforeFullscreen;
        _viewModel.SetFullscreen(false);
        _cursorTimer.Stop();
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
        _cursorTimer.Stop();
        _benchmarkTimeout?.Stop();
        _viewModel.Dispose();
    }
}
