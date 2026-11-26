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
}
