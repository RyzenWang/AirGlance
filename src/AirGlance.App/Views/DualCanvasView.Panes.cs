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

public sealed partial class DualCanvasView
{
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
            // A fixed 2400 x 1600 decode was four to eight times the size a pane actually
            // shows in a normal window; ask for what the pane can display instead.
            var (targetWidth, targetHeight) = PanePreviewTarget(pane);
            var decoded = await Task.Run(() =>
            {
                var metadata = ImageDecoder.ReadMetadata(path);
                var animation = metadata.IsAnimated
                    ? ImageDecoder.DecodeAnimation(path, targetWidth, targetHeight, token)
                    : null;
                var still = animation is null ? ImageDecoder.Decode(path, targetWidth, targetHeight, token) : null;
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
}
