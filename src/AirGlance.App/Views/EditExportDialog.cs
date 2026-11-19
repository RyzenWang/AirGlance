using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AirGlance.App.Views;

/// <summary>
/// Full-window export modal. It intentionally uses the same paper panel and
/// segmented controls as the edit drawer instead of the platform title bar.
/// </summary>
internal sealed class EditExportDialog : Window
{
    private readonly AppServices _services;
    private readonly string _sourcePath;
    private readonly Func<EditExportOptions, string, Task<string>> _export;
    private readonly ComboBox _resize;
    private readonly TextBox _resizeValue;
    private readonly TextBox _fileName;
    private readonly CheckBox _metadata;
    private readonly CoralSlider _quality;
    private readonly TextBlock _qualityValue;
    private readonly TextBlock _hint;
    private readonly TextBlock _status;
    private readonly Button _exportButton;
    private readonly Button[] _formatButtons;
    private string _folder;
    private int _formatIndex;
    private bool _busy;

    private static readonly ExportFormat[] Formats = [ExportFormat.Jpeg, ExportFormat.Png, ExportFormat.WebP];

    public EditExportDialog(
        AppServices services,
        string sourcePath,
        IImageBrushSource? preview,
        CropRegion crop,
        int cropWidth,
        int cropHeight,
        EditExportOptions defaults,
        Func<EditExportOptions, string, Task<string>> export)
    {
        _services = services;
        _sourcePath = sourcePath;
        _export = export;
        _folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Environment.CurrentDirectory;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Width = 790;
        Height = 500;
        Focusable = true;
        Resources["SystemAccentColor"] = Color.Parse("#E3563B");
        Resources["SystemAccentColorLight1"] = Color.Parse("#EE7B61");
        Resources["SystemAccentColorDark1"] = Color.Parse("#C94F39");
        Resources["SliderTrackValueFill"] = Res("Coral");
        Resources["SliderTrackValueFillPointerOver"] = Res("Coral");
        Resources["SliderThumbBackground"] = Res("Coral");
        Resources["SliderThumbBackgroundPointerOver"] = Res("Coral");
        Resources["CheckBoxCheckBackgroundFillChecked"] = Res("Coral");
        Resources["CheckBoxCheckBackgroundStrokeChecked"] = Res("Coral");
        Resources["CheckBoxCheckGlyphForegroundChecked"] = Brushes.White;

        var loc = services.Localization;
        var title = new TextBlock
        {
            Text = loc.T("L.EditExportReady"),
            FontSize = 23,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = -0.4
        };
        var subtitle = new TextBlock
        {
            Text = loc.T("L.EditExportSub"),
            FontSize = 11.5,
            Foreground = Res("InkSecondary"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var close = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry("IconClose"),
                Width = 15,
                Height = 15,
                Stroke = Res("InkSecondary"),
                StrokeThickness = 1.7
            },
            Classes = { "icon" },
            Width = 30,
            Height = 30,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => Close();

        var headerText = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = loc.T("L.EditExportTitle").ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    LetterSpacing = 1.4,
                    Foreground = Res("Coral")
                },
                title,
                subtitle
            }
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(headerText);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,*"),
            ColumnSpacing = 25,
            Margin = new Thickness(0, 23, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        body.Children.Add(BuildPreview(preview, crop, cropWidth, cropHeight, loc));

        var settings = new StackPanel
        {
            Spacing = 13,
            VerticalAlignment = VerticalAlignment.Top
        };
        _formatButtons = new Button[3];
        var segmentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 3
        };
        var segmented = new Border
        {
            Height = 30,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#EEE7DD")),
            ClipToBounds = true,
            Child = segmentGrid
        };
        for (var index = 0; index < Formats.Length; index++)
        {
            var formatIndex = index;
            var button = new Button
            {
                    Content = Centered(Formats[index].DisplayName(), 11, FontWeight.SemiBold),
                Classes = { "export-segment" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            button.Click += (_, _) => SelectFormat(formatIndex);
            Grid.SetColumn(button, index);
            _formatButtons[index] = button;
            segmentGrid.Children.Add(button);
        }

        _quality = new CoralSlider
        {
            Value = defaults.NormalizedQuality,
            Margin = new Thickness(0, 2, 0, 0)
        };
        _qualityValue = new TextBlock
        {
            Text = defaults.NormalizedQuality.ToString(),
            FontSize = 11,
            Foreground = Res("InkSecondary")
        };
        var qualityHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        qualityHeader.Children.Add(Label(loc.T("L.EditQuality")));
        Grid.SetColumn(_qualityValue, 1);
        qualityHeader.Children.Add(_qualityValue);

        _resize = new ComboBox
        {
            ItemsSource = new[]
            {
                loc.T("L.EditOriginalPixels"),
                loc.T("L.EditLongEdge"),
                loc.T("L.EditFitWithin")
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _resizeValue = new TextBox
        {
            Text = Math.Max(64, defaults.ResizeValue).ToString(),
            Width = 92,
            IsEnabled = false,
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var resizeRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        resizeRow.Children.Add(_resize);
        Grid.SetColumn(_resizeValue, 1);
        resizeRow.Children.Add(_resizeValue);

        _fileName = new TextBox
        {
            Text = Path.GetFileNameWithoutExtension(sourcePath) + "-edited.jpg"
        };
        _metadata = new CheckBox
        {
            Content = loc.T("L.EditKeepMetadata"),
            IsChecked = defaults.KeepMetadata
        };
        _hint = new TextBlock
        {
            FontSize = 10.5,
            Foreground = Res("InkTertiary"),
            VerticalAlignment = VerticalAlignment.Center
        };
        _status = new TextBlock
        {
            FontSize = 10.5,
            Foreground = Res("InkSecondary"),
            VerticalAlignment = VerticalAlignment.Center
        };

        settings.Children.Add(new StackPanel { Children = { Label(loc.T("L.EditFormat")), segmented } });
        settings.Children.Add(new StackPanel { Children = { qualityHeader, _quality } });
        settings.Children.Add(new StackPanel
        {
            Children = { Label(loc.T("L.EditResize")), resizeRow }
        });
        settings.Children.Add(new StackPanel
        {
            Children = { Label(loc.T("L.EditFileName")), _fileName }
        });
        settings.Children.Add(_metadata);
        body.Children.Add(settings);
        Grid.SetColumn(settings, 1);

        var cancelLabel = Centered(loc.T("L.EditCancel"), 12, FontWeight.Normal);
        cancelLabel.Width = 118;
        var cancel = new Button
        {
            Content = cancelLabel,
            Classes = { "edit-action" },
            Width = 118,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        cancel.Click += (_, _) => Close();
        var exportLabel = Centered(loc.T("L.EditExportCopy"), 12, FontWeight.SemiBold);
        exportLabel.Width = 134;
        _exportButton = new Button
        {
            Content = exportLabel,
            Classes = { "edit-primary" },
            Width = 134,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsDefault = true
        };
        _exportButton.Click += OnExportClick;

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 18, 0, 0)
        };
        var hintPanel = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _status, _hint }
        };
        footer.Children.Add(hintPanel);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { cancel, _exportButton }
        };
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        content.Children.Add(header);
        Grid.SetRow(body, 1);
        content.Children.Add(body);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);

        var modal = new Border
        {
            Width = 790,
            Height = 500,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(26, 24, 26, 20),
            Background = Res("Surface"),
            BorderBrush = Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 70,
                OffsetY = 24,
                Color = Color.FromArgb(90, 30, 22, 16)
            }),
            Child = content
        };

        Content = new Grid { Children = { modal } };

        _quality.ValueChanged += (_, _) =>
        {
            _qualityValue.Text = ((int)Math.Round(_quality.Value)).ToString();
            SyncHint();
        };
        _resize.SelectionChanged += (_, _) =>
        {
            var original = _resize.SelectedIndex == 0;
            _resizeValue.IsEnabled = !original;
            _resizeValue.Opacity = original ? 0.45 : 1;
            SyncHint();
        };
        _resizeValue.TextChanged += (_, _) => SyncHint();
        _fileName.TextChanged += (_, _) => SyncHint();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        SelectFormat(0);
    }

    public string? ExportedPath { get; private set; }

    private Control BuildPreview(
        IImageBrushSource? preview,
        CropRegion crop,
        int cropWidth,
        int cropHeight,
        LocalizationService loc)
    {
        var image = new Border
        {
            Height = 250,
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
            Background = Res("Surface")
        };
        if (preview is not null)
        {
            image.Background = new ImageBrush(preview)
            {
                Stretch = Stretch.Uniform,
                SourceRect = new RelativeRect(
                    crop.X,
                    crop.Y,
                    crop.Width,
                    crop.Height,
                    RelativeUnit.Relative)
            };
        }

        var caption = new TextBlock
        {
            Text = $"{cropWidth} × {cropHeight} · cropped preview",
            FontSize = 10,
            Foreground = Res("InkTertiary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var frame = new Border
        {
            Background = Res("SurfaceAlt"),
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(9),
            Child = new StackPanel { Children = { image, caption } }
        };
        return frame;
    }

    private void SelectFormat(int index)
    {
        _formatIndex = Math.Clamp(index, 0, 2);
        for (var i = 0; i < _formatButtons.Length; i++)
        {
            _formatButtons[i].Classes.Set("active", i == _formatIndex);
        }

        var extension = Formats[_formatIndex].Extension();
        _fileName.Text = Path.GetFileNameWithoutExtension(_fileName.Text) + extension;
        var isPng = Formats[_formatIndex] == ExportFormat.Png;
        _quality.IsEnabled = !isPng;
        _quality.Opacity = isPng ? 0.4 : 1;
        SyncHint();
    }

    private void SyncHint()
    {
        var format = Formats[_formatIndex];
        var mode = _resize.SelectedIndex switch
        {
            1 => _services.Localization.T("L.EditLongEdge"),
            2 => _services.Localization.T("L.EditFitWithin"),
            _ => _services.Localization.T("L.EditOriginalPixels")
        };
        _hint.Text = format == ExportFormat.Png
            ? $"{format.DisplayName()} · {mode}"
            : $"{format.DisplayName()} · quality {(int)Math.Round(_quality.Value)} · {mode}";
        _status.Text = string.Empty;
    }

    private async void OnExportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var format = Formats[_formatIndex];
        var name = Path.GetFileNameWithoutExtension((_fileName.Text ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(_sourcePath) + "-edited";
        }

        var directory = Path.GetFullPath(_folder);
        if (!Directory.Exists(directory))
        {
            _status.Text = _services.Localization.T("L.EditExportFailed");
            return;
        }

        var target = ImageExporter.NextAvailablePath(Path.Combine(directory, name + format.Extension()));
        var resizeValue = int.TryParse(_resizeValue.Text, out var parsed)
            ? Math.Clamp(parsed, 64, 200_000)
            : 4096;
        var options = new EditExportOptions(
            format,
            (int)Math.Round(_quality.Value),
            _resize.SelectedIndex == 1
                ? EditResizeMode.LongEdge
                : _resize.SelectedIndex == 2
                    ? EditResizeMode.FitWithin
                    : EditResizeMode.Original,
            resizeValue,
            _metadata.IsChecked == true);

        _busy = true;
        _exportButton.IsEnabled = false;
        _status.Text = _services.Localization.T("L.EditExporting");
        try
        {
            ExportedPath = await _export(options, target);
            Close(ExportedPath);
        }
        catch (OperationCanceledException)
        {
            _status.Text = string.Empty;
        }
        catch
        {
            _status.Text = _services.Localization.T("L.EditExportFailed");
        }
        finally
        {
            _busy = false;
            _exportButton.IsEnabled = true;
        }
    }

    private TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.8,
        Foreground = Res("InkSecondary"),
        Margin = new Thickness(0, 0, 0, 5)
    };

    private static TextBlock Centered(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static IBrush Res(string key) => key switch
    {
        "Surface" => new SolidColorBrush(Color.Parse("#FBFAF7")),
        "SurfaceAlt" => new SolidColorBrush(Color.Parse("#F6F2EB")),
        "Border" => new SolidColorBrush(Color.Parse("#E3DAD0")),
        "Ink" => new SolidColorBrush(Color.Parse("#23211F")),
        "InkSecondary" => new SolidColorBrush(Color.Parse("#69645C")),
        "InkTertiary" => new SolidColorBrush(Color.Parse("#9A9286")),
        "Coral" => new SolidColorBrush(Color.Parse("#E3563B")),
        "OnCoral" => Brushes.White,
        _ => Brushes.Transparent
    };

    private static Geometry FindGeometry(string key) =>
        Application.Current?.FindResource(key) as Geometry
        ?? StreamGeometry.Parse("M 0 0");
}
