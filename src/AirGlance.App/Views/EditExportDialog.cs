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
/// Export dialog for the edit session. It collects format, quality, resize and
/// metadata choices, then lets the window run the actual encode.
/// </summary>
internal sealed class EditExportDialog : Window
{
    private readonly AppServices _services;
    private readonly string _sourcePath;
    private readonly Func<EditExportOptions, string, Task<string>> _export;
    private readonly ComboBox _format;
    private readonly Slider _quality;
    private readonly TextBlock _qualityValue;
    private readonly ComboBox _resize;
    private readonly TextBox _resizeValue;
    private readonly TextBox _fileName;
    private readonly CheckBox _metadata;
    private readonly TextBlock _folderText;
    private readonly TextBlock _status;
    private readonly Button _exportButton;
    private string _folder;
    private bool _busy;

    public EditExportDialog(
        AppServices services,
        string sourcePath,
        IImage? preview,
        EditExportOptions defaults,
        Func<EditExportOptions, string, Task<string>> export)
    {
        _services = services;
        _sourcePath = sourcePath;
        _export = export;
        _folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Environment.CurrentDirectory;

        Title = services.Localization.T("L.EditExportTitle");
        Width = 760;
        Height = 590;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.Parse("#FBFAF7"));

        var root = new Border
        {
            Padding = new Thickness(26),
            Child = BuildContent(services.Localization, preview, defaults, out _format, out _quality,
                out _qualityValue, out _resize, out _resizeValue, out _fileName, out _metadata,
                out _folderText, out _status, out _exportButton)
        };

        Content = root;
        _exportButton.Click += OnExportClick;
        _format.SelectionChanged += (_, _) => SyncFormat();
        _quality.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == "Value")
            {
                _qualityValue.Text = ((int)_quality.Value).ToString();
                SyncHint();
            }
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
            if (e.Key == Key.Enter)
            {
                OnExportClick(this, new Avalonia.Interactivity.RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        SyncFormat();
    }

    public string? ExportedPath { get; private set; }

    private Control BuildContent(
        LocalizationService loc,
        IImage? preview,
        EditExportOptions defaults,
        out ComboBox format,
        out Slider quality,
        out TextBlock qualityValue,
        out ComboBox resize,
        out TextBox resizeValue,
        out TextBox fileName,
        out CheckBox metadata,
        out TextBlock folderText,
        out TextBlock status,
        out Button exportButton)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        var title = new TextBlock
        {
            Text = loc.T("L.EditExportReady"),
            FontSize = 22,
            FontWeight = FontWeight.SemiBold
        };
        var subtitle = new TextBlock
        {
            Text = loc.T("L.EditExportSub"),
            Foreground = Res("InkSecondary"),
            FontSize = 11.5,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var header = new StackPanel
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
        grid.Children.Add(header);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,*"),
            ColumnSpacing = 24,
            Margin = new Thickness(0, 24, 0, 0)
        };
        var previewBorder = new Border
        {
            Background = Res("SurfaceAlt"),
            BorderBrush = Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(9),
            Child = new StackPanel
            {
                Children =
                {
                    new Border
                    {
                        Height = 215,
                        CornerRadius = new CornerRadius(7),
                        ClipToBounds = true,
                        Background = Res("Surface"),
                        Child = preview is null
                            ? null
                            : new Image
                            {
                                Source = preview,
                                Stretch = Stretch.Uniform
                            }
                    },
                    new TextBlock
                    {
                        Text = loc.T("L.EditOriginalUnchanged"),
                        FontSize = 10,
                        Foreground = Res("InkTertiary"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 8, 0, 0)
                    }
                }
            }
        };
        body.Children.Add(previewBorder);

        var settings = new StackPanel { Spacing = 13 };
        format = new ComboBox
        {
            ItemsSource = new[] { "JPEG", "PNG", "WebP" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        quality = new Slider
        {
            Minimum = 40,
            Maximum = 100,
            Value = defaults.NormalizedQuality,
            TickFrequency = 1,
            IsSnapToTickEnabled = true
        };
        qualityValue = new TextBlock
        {
            Text = defaults.NormalizedQuality.ToString(),
            Foreground = Res("InkSecondary"),
            FontSize = 11
        };
        var qualityHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        qualityHeader.Children.Add(Label(loc.T("L.EditQuality")));
        Grid.SetColumn(qualityValue, 1);
        qualityHeader.Children.Add(qualityValue);
        var qualityPanel = new StackPanel { Children = { qualityHeader, quality } };

        resize = new ComboBox
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
        resizeValue = new TextBox
        {
            Text = Math.Max(64, defaults.ResizeValue).ToString(),
            Width = 96,
            IsEnabled = false,
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var resizeRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        resizeRow.Children.Add(resize);
        Grid.SetColumn(resizeValue, 1);
        resizeRow.Children.Add(resizeValue);
        var resizePanel = new StackPanel
        {
            Children = { Label(loc.T("L.EditResize")), resizeRow }
        };

        fileName = new TextBox
        {
            Text = Path.GetFileNameWithoutExtension(_sourcePath) + "-edited.jpg"
        };
        var filePanel = new StackPanel
        {
            Children = { Label(loc.T("L.EditFileName")), fileName }
        };

        metadata = new CheckBox
        {
            Content = loc.T("L.EditKeepMetadata"),
            IsChecked = defaults.KeepMetadata
        };
        folderText = new TextBlock
        {
            Text = _folder,
            Foreground = Res("InkSecondary"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var folderButton = new Button
        {
            Content = loc.T("L.EditChooseFolder"),
            Classes = { "edit-action" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = 30,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var folderLabel = folderText;
        folderButton.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = loc.T("L.EditChooseFolder"),
                AllowMultiple = false
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _folder = path;
                folderLabel.Text = path;
            }
        };

        settings.Children.Add(new StackPanel { Children = { Label(loc.T("L.EditFormat")), format } });
        settings.Children.Add(qualityPanel);
        settings.Children.Add(resizePanel);
        settings.Children.Add(filePanel);
        settings.Children.Add(metadata);
        settings.Children.Add(new StackPanel { Children = { folderButton, folderText } });
        body.Children.Add(settings);
        Grid.SetColumn(settings, 1);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        status = new TextBlock
        {
            Text = string.Empty,
            Foreground = Res("InkSecondary"),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        var cancel = new Button
        {
            Content = loc.T("L.EditCancel"),
            Classes = { "edit-action" },
            Width = 118
        };
        cancel.Click += (_, _) => Close();
        exportButton = new Button
        {
            Content = loc.T("L.EditExportCopy"),
            Classes = { "edit-primary" },
            Width = 132,
            IsDefault = true
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, exportButton }
        };
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 18, 0, 0)
        };
        footer.Children.Add(status);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        return grid;
    }

    private void SyncFormat()
    {
        var format = (ExportFormat)Math.Clamp(_format.SelectedIndex, 0, 2);
        var ext = format.Extension();
        _fileName.Text = Path.GetFileNameWithoutExtension(_fileName.Text) + ext;
        _quality.IsEnabled = format != ExportFormat.Png;
        _quality.Opacity = format == ExportFormat.Png ? 0.4 : 1;
        SyncHint();
    }

    private void SyncHint()
    {
        var format = (ExportFormat)Math.Clamp(_format.SelectedIndex, 0, 2);
        var quality = (int)Math.Round(_quality.Value);
        var mode = _resize.SelectedIndex == 1
            ? _services.Localization.T("L.EditLongEdge")
            : _resize.SelectedIndex == 2
                ? _services.Localization.T("L.EditFitWithin")
                : _services.Localization.T("L.EditOriginalPixels");
        _status.Text = $"{format.DisplayName()} · {mode}" +
                       (format == ExportFormat.Png ? string.Empty : $" · {quality}");
    }

    private async void OnExportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var format = (ExportFormat)Math.Clamp(_format.SelectedIndex, 0, 2);
        var extension = format.Extension();
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

        var target = ImageExporter.NextAvailablePath(Path.Combine(directory, name + extension));
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
}
