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
}
