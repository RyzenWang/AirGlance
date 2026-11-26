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
    private void SetFilmstripVisible(bool visible)
    {
        if (visible && !HasAnyImage)
        {
            return;
        }

        ShowChrome();
        _filmstripVisible = visible;
        if (visible)
        {
            SetInfoVisible(false);
            _ = RebuildFilmstripAsync();
        }
        else
        {
            _filmstripCts?.Cancel();
        }

        _filmstrip.IsVisible = visible;
        UpdateDock();
    }

    private async Task RebuildFilmstripAsync()
    {
        _filmstripCts?.Cancel();
        _filmstripCts?.Dispose();
        _filmstripCts = new CancellationTokenSource();
        var token = _filmstripCts.Token;
        var version = ++_filmstripVersion;

        _filmA.Thumbs.Children.Clear();
        _filmB.Thumbs.Children.Clear();
        foreach (var bitmap in _thumbnailBitmaps)
        {
            bitmap.Dispose();
        }

        _thumbnailBitmaps.Clear();
        if (!_filmstripVisible || _disposed)
        {
            return;
        }

        await PopulateFilmHalfAsync(_filmA, _a, token, version);
        await PopulateFilmHalfAsync(_filmB, _b, token, version);
        UpdateFilmstripActive();
    }

    private async Task PopulateFilmHalfAsync(FilmHalf half, PaneSlot pane, CancellationToken token, int version)
    {
        var empty = pane.CurrentPath is null;
        half.FolderName.Text = empty || pane.Folder is null ? string.Empty : Path.GetFileName(pane.Folder);
        half.Count.Text = empty
            ? string.Empty
            : string.Format(_services.Localization.T("L.ImageCount"), pane.Files.Count);
        half.Scroll.IsVisible = !empty;
        half.Host.IsHitTestVisible = !empty;
        if (empty)
        {
            return;
        }

        var files = pane.Files.Take(24).ToList();
        for (var index = 0; index < files.Count; index++)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var itemIndex = index;
            var image = new Image { Stretch = Stretch.UniformToFill };
            var accent = new SolidColorBrush(Color.Parse(pane.PaneNumber == 0 ? "#E3563B" : "#2E6F63"));
            var thumb = new Border
            {
                Width = 88,
                Height = 54,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#20262E")),
                ClipToBounds = true,
                BorderThickness = new Thickness(2),
                BorderBrush = itemIndex == pane.FileIndex ? accent : Brushes.Transparent,
                Child = image
            };
            var frame = new Grid { Width = 88, Height = 54 };
            frame.Children.Add(thumb);
            if (itemIndex == pane.FileIndex)
            {
                frame.Children.Add(CreateThumbBadge(pane.PaneNumber == 0 ? "A" : "B", pane.PaneNumber == 0));
            }

            var label = new TextBlock
            {
                Text = Path.GetFileName(files[itemIndex]),
                FontSize = 9,
                MaxWidth = 88,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.Parse("#BEB6A9"))
            };
            var button = new Button
            {
                Content = new StackPanel { Spacing = 3, Children = { frame, label } },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Width = 92
            };
            button.Click += (_, _) =>
            {
                SetActive(pane.PaneNumber);
                pane.FileIndex = itemIndex;
                pane.CurrentPath = pane.Files[itemIndex];
                _ = ApplySourceAsync(pane, new ImageSource(pane.Folder ?? string.Empty, pane.Files, itemIndex), itemIndex);
            };
            half.Thumbs.Children.Add(button);

            try
            {
                var path = files[itemIndex];
                var decoded = await Task.Run(() => ImageDecoder.Decode(path, 160, 160, token), token);
                if (token.IsCancellationRequested || version != _filmstripVersion)
                {
                    return;
                }

                var bitmap = BitmapFactory.FromDecoded(decoded);
                _thumbnailBitmaps.Add(bitmap);
                image.Source = bitmap;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A broken thumbnail must not remove the filmstrip item.
            }
        }
    }

    private void UpdateFilmstripActive()
    {
        _filmA.Host.Classes.Set("active", _activeIndex == 0);
        _filmB.Host.Classes.Set("active", _activeIndex == 1);
        _filmA.Badge.Classes.Set("active", _activeIndex == 0);
        _filmB.Badge.Classes.Set("active", _activeIndex == 1);
    }
}
