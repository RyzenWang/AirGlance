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
    private void OpenPaneMenu(int paneIndex, Point point)
    {
        SetActive(paneIndex);
        ShowChrome();
        _menuPane = paneIndex;
        var pane = Pane(paneIndex);
        _menuBadge.Classes.Set("a", paneIndex == 0);
        _menuBadge.Classes.Set("b", paneIndex == 1);
        _menuBadgeText.Text = paneIndex == 0 ? "A" : "B";
        _menuFolder.Text = pane.Folder is null ? _services.Localization.T("L.NoFolder") : Path.GetFileName(pane.Folder);
        _menuMeta.Text = pane.Files.Count > 0
            ? $"{pane.FileIndex + 1:D2} / {pane.Files.Count:D2}"
            : string.Empty;
        var hasImage = pane.CurrentPath is not null;
        _menuReveal.IsEnabled = hasImage;
        _menuCopyImage.IsEnabled = hasImage;
        _menuCopyPath.IsEnabled = hasImage;
        _menuScrim.IsVisible = true;
        _paneMenu.IsVisible = true;
        _menuVisible = true;
        foreach (var item in _menuItems)
        {
            item.Classes.Remove("hover");
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        var x = Math.Clamp(point.X + 8, 8, Math.Max(8, width - 286));
        var y = Math.Clamp(point.Y + 8, 8, Math.Max(8, height - 360));
        _paneMenu.Margin = new Thickness(x, y, 0, 0);
    }

    private void ClosePaneMenu()
    {
        _menuVisible = false;
        foreach (var item in _menuItems)
        {
            item.Classes.Remove("hover");
        }

        _menuScrim.IsVisible = false;
        _paneMenu.IsVisible = false;
    }

    private void ActivateMenuFolder()
    {
        var pane = _menuPane;
        ClosePaneMenu();
        OpenRequested?.Invoke(this, new DualOpenRequest(pane, true));
    }

    private void ActivateMenuImage()
    {
        var pane = _menuPane;
        ClosePaneMenu();
        OpenRequested?.Invoke(this, new DualOpenRequest(pane, false));
    }

    private void ActivateMenuReveal()
    {
        if (Pane(_menuPane).CurrentPath is not { } path)
        {
            return;
        }

        ClosePaneMenu();
        RevealRequested?.Invoke(this, new DualRevealRequest(path));
    }

    private async Task CopyMenuImageAsync()
    {
        var bitmap = Pane(_menuPane).Bitmap ?? Pane(_menuPane).Animation?.Frames.FirstOrDefault();
        ClosePaneMenu();
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

    private async Task CopyMenuPathAsync()
    {
        var path = Pane(_menuPane).CurrentPath;
        ClosePaneMenu();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (path is null || clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(path);
    }

    private void UpdatePaneLabels(PaneSlot pane)
    {
        pane.FolderCount.Text = pane.Files.Count > 0
            ? pane.Files.Count.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        pane.FileMeta.Text = pane.Metadata is null
            ? string.Empty
            : $"{pane.Metadata.DisplayWidth} × {pane.Metadata.DisplayHeight}";
        pane.FileZoom.Text = pane.Metadata is null
            ? string.Empty
            : $"· {pane.Viewer.ZoomPercent:0}%";
        pane.FileChip.IsVisible = pane.CurrentPath is not null;
        pane.FilePosition.Text = pane.CurrentPath is not null && pane.Files.Count > 0
            ? $"{pane.FileIndex + 1:D2} / {pane.Files.Count:D2}"
            : string.Empty;
        ApplyPaneHeaderLayout(pane);
        ApplyChrome();
    }
}
