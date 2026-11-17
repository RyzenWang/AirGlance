using System.Diagnostics;
using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private readonly DispatcherTimer? _benchmarkTimeout;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private ContextMenu? _contextMenu;
    private FilmstripView? _filmstripView;
    private InfoPanelView? _infoPanel;
    private bool _panelsCreated;
    private bool _isFullscreen;
    private bool _initialZoomApplied;
    private bool _cursorHidden;

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
        InitializeComponent();
        StartupTrace.Mark("window-ctor-xaml");

        _viewModel = new MainWindowViewModel(services, preload);
        DataContext = _viewModel;
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

        if (_benchmark is not null)
        {
            _benchmark.MarkFirstFrame(
                _viewModel.LastDecodeMilliseconds,
                _viewModel.Metadata?.Megapixels ?? 0);
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
        _services.Localization.LanguageChanged -= OnLocalizationChanged;
        _cursorTimer.Stop();
        _benchmarkTimeout?.Stop();
        _viewModel.Dispose();
    }
}
