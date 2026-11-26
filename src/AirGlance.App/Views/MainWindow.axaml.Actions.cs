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

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        _contextMenu = null;
        _analysisMenu = null;
        _eyedropperMenuItem = null;
        CanvasBorder.ContextMenu = null;
        ApplyLanguage();
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
}
