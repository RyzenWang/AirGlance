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
    private void SetInfoVisible(bool visible)
    {
        ShowChrome();
        _infoVisible = visible;
        if (visible)
        {
            SetFilmstripVisible(false);
        }

        _info.IsVisible = visible;
        UpdateDock();
        if (visible)
        {
            UpdateInfo();
        }
    }

    private void UpdateInfo()
    {
        _infoContent.Children.Clear();
        var pane = Active;
        if (pane.Metadata is not { } metadata)
        {
            _infoContent.Children.Add(new TextBlock
            {
                Text = _services.Localization.T("L.NoImage"),
                Foreground = new SolidColorBrush(Color.Parse("#69645C")),
                Margin = new Thickness(0, 12, 0, 0)
            });
            return;
        }

        AddInfoTitle(_infoContent, metadata.FileName);
        AddInfoSection(_infoContent, _services.Localization.T("L.SectionFile"),
            (_services.Localization.T("L.InfoPath"), metadata.FilePath),
            (_services.Localization.T("L.InfoSize"), FormatBytes(metadata.FileSizeBytes)));
        AddInfoSection(_infoContent, _services.Localization.T("L.SectionImage"),
            (_services.Localization.T("L.InfoDimensions"), $"{metadata.DisplayWidth} × {metadata.DisplayHeight}"),
            (_services.Localization.T("L.InfoFormat"), metadata.FormatName),
            (_services.Localization.T("L.InfoBitDepth"), metadata.BitDepth.ToString(CultureInfo.InvariantCulture)));
        AddInfoSection(_infoContent, _services.Localization.T("L.SectionColor"),
            (_services.Localization.T("L.InfoColorSpace"), metadata.ColorSpaceDisplay));
    }

    private static void AddInfoTitle(StackPanel content, string text)
    {
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 8)
        });
    }

    private static void AddInfoSection(StackPanel content, string title, params (string Label, string Value)[] rows)
    {
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#9A9286")),
            Margin = new Thickness(0, 10, 0, 4)
        });
        foreach (var (label, value) in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("82,*"), Margin = new Thickness(0, 2) };
            grid.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.Parse("#69645C"))
            });
            var valueText = new TextBlock
            {
                Text = value,
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
                FontSize = 9.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);
            content.Children.Add(grid);
        }
    }
}
