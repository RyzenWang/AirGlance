using AirGlance.App.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AirGlance.App.Controls;

/// <summary>
/// Floating toolbar shown only while the crop canvas is immersive. It replaces the
/// right edit drawer and keeps every crop handle clear of chrome.
/// </summary>
internal sealed class CropToolbarView : UserControl
{
    private readonly LocalizationService _localization;
    private readonly TextBlock _size;
    private readonly TextBlock _sizeLabel;
    private readonly TextBlock _resetLabel;
    private readonly TextBlock _cancelLabel;
    private readonly TextBlock _doneLabel;
    private readonly Button[] _ratioButtons;

    public CropToolbarView(LocalizationService localization)
    {
        _localization = localization;

        _sizeLabel = new TextBlock
        {
            Text = "CROP SIZE",
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.1,
            Foreground = new SolidColorBrush(Color.Parse("#9A9286"))
        };
        _size = new TextBlock
        {
            Text = "2400 × 1350",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#23211F")),
        };
        var sizeBlock = new StackPanel
        {
            MinWidth = 112,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _sizeLabel, _size }
        };

        var ratios = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        _ratioButtons =
        [
            Ratio("free", out var free),
            Ratio("original", out var original),
            Ratio("1:1", out var square),
            Ratio("4:3", out var fourThree),
            Ratio("16:9", out var sixteenNine)
        ];
        foreach (var button in _ratioButtons)
        {
            ratios.Children.Add(button);
        }

        _resetLabel = Centered(_localization.T("L.EditReset"), 11, FontWeight.Normal);
        var reset = new Button
        {
            Content = _resetLabel,
            Classes = { "crop-bar-reset" },
            Width = 58,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        reset.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);

        _cancelLabel = Centered(_localization.T("L.EditCancel"), 11, FontWeight.Medium);
        var cancel = new Button
        {
            Content = _cancelLabel,
            Classes = { "crop-bar-action" },
            Width = 74,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        _doneLabel = Centered(_localization.T("L.EditDone"), 11, FontWeight.SemiBold);
        var done = new Button
        {
            Content = _doneLabel,
            Classes = { "crop-bar-primary" },
            Width = 74,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        done.Click += (_, _) => DoneRequested?.Invoke(this, EventArgs.Empty);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 12
        };
        grid.Children.Add(sizeBlock);
        grid.Children.Add(Divider());
        Grid.SetColumn(ratios, 2);
        grid.Children.Add(ratios);
        Grid.SetColumn(reset, 3);
        grid.Children.Add(reset);
        Grid.SetColumn(cancel, 4);
        grid.Children.Add(cancel);
        Grid.SetColumn(done, 5);
        grid.Children.Add(done);

        var bar = new Border
        {
            Classes = { "crop-bar" },
            Child = grid
        };
        Content = bar;
        ApplyLanguage();
    }

    public event EventHandler? ResetRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? DoneRequested;
    public event EventHandler<string>? AspectRequested;

    public void ApplyLanguage()
    {
        _sizeLabel.Text = _localization.T("L.EditCropSizeLabel");
        _resetLabel.Text = _localization.T("L.EditReset");
        _cancelLabel.Text = _localization.T("L.EditCancel");
        _doneLabel.Text = _localization.T("L.EditDone");
        _ratioButtons[0].Content = Centered(_localization.T("L.EditFree"), 11, FontWeight.Normal, 42);
        _ratioButtons[1].Content = Centered(_localization.T("L.EditOriginalRatio"), 11, FontWeight.Normal, 58);
        _ratioButtons[2].Content = Centered("1:1", 11, FontWeight.Normal, 34);
        _ratioButtons[3].Content = Centered("4:3", 11, FontWeight.Normal, 34);
        _ratioButtons[4].Content = Centered("16:9", 11, FontWeight.Normal, 42);
    }

    public void SetSize(string text) => _size.Text = text;

    public void SetActiveRatio(string ratio)
    {
        foreach (var button in _ratioButtons)
        {
            button.Classes.Set("active", string.Equals(button.CommandParameter as string, ratio, StringComparison.Ordinal));
        }
    }

    private Button Ratio(string ratio, out Button button)
    {
        button = new Button
        {
            Content = Centered(ratio, 11, FontWeight.Normal, 42),
            CommandParameter = ratio,
            Classes = { "crop-bar-chip" }
        };
        button.Click += (_, _) => AspectRequested?.Invoke(this, ratio);
        return button;
    }

    private static TextBlock Centered(string text, double size, FontWeight weight, double width = 0) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
        Width = width > 0 ? width : double.NaN
    };

    private static Border Divider() => new()
    {
        Width = 1,
        Height = 34,
        Background = new SolidColorBrush(Color.Parse("#E0D9CD")),
        Margin = new Thickness(2, 0, 2, 0)
    };
}
