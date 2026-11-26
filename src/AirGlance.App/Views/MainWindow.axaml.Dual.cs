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
}
