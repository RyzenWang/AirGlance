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

public partial class MainWindow
{
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
}
