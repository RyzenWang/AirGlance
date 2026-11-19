using AirGlance.App.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AirGlance.App.Controls;

/// <summary>
/// Right-hand non-destructive edit drawer. It only raises intent events; the window
/// owns the image, crop overlay and export dialog.
/// </summary>
internal sealed class EditPanelView : UserControl
{
    private readonly LocalizationService _localization;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _transformLabel;
    private readonly TextBlock _cropLabel;
    private readonly TextBlock _exportLabel;
    private readonly TextBlock _cropSize;
    private readonly TextBlock _safeNote;
    private readonly TextBlock _exportCardTitle;
    private readonly TextBlock _exportCardSub;
    private readonly Button _resetButton;
    private readonly Button _resetCropButton;
    private readonly Button _rotateLeft;
    private readonly Button _rotateRight;
    private readonly Button _flipH;
    private readonly Button _flipV;
    private readonly TextBlock _rotateLeftLabel;
    private readonly TextBlock _rotateRightLabel;
    private readonly TextBlock _flipHLabel;
    private readonly TextBlock _flipVLabel;
    private readonly Button[] _ratioButtons;
    private readonly Button _cropButton;
    private readonly TextBlock _cropButtonLabel;
    private readonly Button _exportCard;
    private readonly Button _cancel;
    private readonly Button _export;
    private readonly TextBlock _cancelLabel;
    private readonly TextBlock _exportLabelControl;

    public EditPanelView(LocalizationService localization)
    {
        _localization = localization;
        SetValue(Panel.ZIndexProperty, 35);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        _title = new TextBlock { Classes = { "edit-title" } };
        _subtitle = new TextBlock { Classes = { "edit-sub" }, Margin = new Thickness(0, 5, 0, 0) };
        var eyebrow = new TextBlock { Text = "NON-DESTRUCTIVE", Classes = { "edit-eyebrow" } };
        var close = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry("IconClose"),
                Width = 15,
                Height = 15,
                Stroke = new SolidColorBrush(Color.Parse("#69645C")),
                StrokeThickness = 1.7
            },
            Classes = { "icon" },
            Width = 30,
            Height = 30,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        var headerText = new StackPanel { Children = { eyebrow, _title, _subtitle } };
        headerGrid.Children.Add(headerText);
        Grid.SetColumn(close, 1);
        headerGrid.Children.Add(close);
        var header = new Border
        {
            Padding = new Thickness(20, 20, 16, 14),
            Child = headerGrid
        };
        grid.Children.Add(header);

        var body = new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(20, 0, 20, 0),
            Width = 310,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _transformLabel = SectionLabel(out _resetButton, out var transformHeader);
        _resetButton.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
        body.Children.Add(transformHeader);

        _rotateLeft = ToolButton("L.EditRotateLeft", "IconRotateLeft", out _rotateLeftLabel);
        _rotateLeft.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.Left);
        _rotateRight = ToolButton("L.EditRotateRight", "IconRotateRight", out _rotateRightLabel);
        _rotateRight.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.Right);
        _flipH = ToolButton("L.EditFlipH", "IconFlipHorizontal", out _flipHLabel);
        _flipH.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.FlipH);
        _flipV = ToolButton("L.EditFlipV", "IconFlipVertical", out _flipVLabel);
        _flipV.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.FlipV);
        var tools = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 6
        };
        var toolButtons = new[] { _rotateLeft, _rotateRight, _flipH, _flipV };
        for (var index = 0; index < toolButtons.Length; index++)
        {
            Grid.SetColumn(toolButtons[index], index);
            toolButtons[index].HorizontalAlignment = HorizontalAlignment.Stretch;
            tools.Children.Add(toolButtons[index]);
        }

        body.Children.Add(tools);
        body.Children.Add(Separator());

        _cropLabel = SectionLabel(out _resetCropButton, out var cropHeader);
        _resetCropButton.Click += (_, _) => ResetCropRequested?.Invoke(this, EventArgs.Empty);
        body.Children.Add(cropHeader);

        _cropButtonLabel = CenteredLabel(_localization.T("L.EditCropEnter"), 12, FontWeight.SemiBold);
        _cropButton = new Button
        {
            Content = _cropButtonLabel,
            Classes = { "edit-crop-entry" },
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 9)
        };
        _cropButton.Click += (_, _) => CropRequested?.Invoke(this, EventArgs.Empty);
        body.Children.Add(_cropButton);

        var ratios = new WrapPanel();
        _ratioButtons =
        [
            RatioButton("free", out var free),
            RatioButton("original", out var original),
            RatioButton("1:1", out var square),
            RatioButton("4:3", out var fourThree),
            RatioButton("16:9", out var sixteenNine)
        ];
        foreach (var button in _ratioButtons)
        {
            ratios.Children.Add(button);
        }

        body.Children.Add(ratios);

        _cropSize = new TextBlock
        {
            Classes = { "caption" },
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0)
        };
        body.Children.Add(_cropSize);
        body.Children.Add(Separator());

        _exportLabel = SectionLabel(out _, out var exportHeader, showAction: false);
        body.Children.Add(exportHeader);
        _exportCardTitle = new TextBlock
        {
            Text = _localization.T("L.EditExportCopy"),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold
        };
        _exportCardSub = new TextBlock
        {
            Text = _localization.T("L.EditExportCard"),
            Classes = { "caption" },
            FontSize = 10.5,
            Margin = new Thickness(0, 3, 0, 0)
        };
        var exportIcon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#FBE9E2")),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new IconPath
            {
                Data = FindGeometry("IconDownload"),
                Width = 18,
                Height = 18,
                Stroke = new SolidColorBrush(Color.Parse("#D65A43")),
                StrokeThickness = 1.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var exportText = new StackPanel
        {
            Children = { _exportCardTitle, _exportCardSub },
            VerticalAlignment = VerticalAlignment.Center
        };
        var exportChevron = new IconPath
        {
            Data = FindGeometry("IconChevronRight"),
            Width = 16,
            Height = 16,
            Stroke = new SolidColorBrush(Color.Parse("#948A7E")),
            StrokeThickness = 1.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        var exportContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        exportContent.Children.Add(exportIcon);
        Grid.SetColumn(exportText, 1);
        exportContent.Children.Add(exportText);
        Grid.SetColumn(exportChevron, 2);
        exportContent.Children.Add(exportChevron);
        _exportCard = new Button
        {
            Classes = { "edit-card" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = exportContent
        };
        _exportCard.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        body.Children.Add(_exportCard);

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        _safeNote = new TextBlock
        {
            Text = _localization.T("L.EditOriginalUnchanged"),
            Classes = { "caption" },
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        var safeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 9),
            Children =
            {
                new IconPath
                {
                    Data = FindGeometry("IconShieldCheck"),
                    Width = 14,
                    Height = 14,
                    Stroke = new SolidColorBrush(Color.Parse("#237F76")),
                    StrokeThickness = 1.7,
                    VerticalAlignment = VerticalAlignment.Center
                },
                _safeNote
            }
        };
        _cancelLabel = CenteredLabel(_localization.T("L.EditCancel"), 12, FontWeight.Normal);
        _cancel = new Button
        {
            Content = _cancelLabel,
            Classes = { "edit-action" },
            Width = 128,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _exportLabelControl = CenteredLabel(_localization.T("L.EditExportCopy"), 12, FontWeight.SemiBold);
        _exportLabelControl.Width = 174;
        _export = new Button
        {
            Content = _exportLabelControl,
            Classes = { "edit-primary" },
            Width = 174,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _export.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("128,174"),
            ColumnSpacing = 8
        };
        actions.Children.Add(_cancel);
        Grid.SetColumn(_export, 1);
        actions.Children.Add(_export);
        var footer = new Border
        {
            Classes = { "edit-footer" },
            Child = new StackPanel { Children = { safeRow, actions } }
        };
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        var panelBorder = new Border
        {
            Width = 350,
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 82, 22, 82)
        };
        panelBorder.Classes.Add("edit-panel");
        Content = panelBorder;
        ApplyLanguage();
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler? ResetCropRequested;
    public event EventHandler? CropRequested;
    public event EventHandler<EditAction>? TransformRequested;
    public event EventHandler<string>? CropAspectRequested;

    public enum EditAction
    {
        Left,
        Right,
        FlipH,
        FlipV
    }

    public void ApplyLanguage()
    {
        _title.Text = _localization.T("L.EditTitle");
        _subtitle.Text = _localization.T("L.EditSub");
        _transformLabel.Text = _localization.T("L.EditTransform");
        _cropLabel.Text = _localization.T("L.EditCrop");
        _exportLabel.Text = _localization.T("L.EditExport");
        _resetButton.Content = _localization.T("L.EditReset");
        _resetCropButton.Content = _localization.T("L.EditResetCrop");
        _rotateLeftLabel.Text = _localization.T("L.EditRotateLeft");
        _rotateRightLabel.Text = _localization.T("L.EditRotateRight");
        _flipHLabel.Text = _localization.T("L.EditFlipH");
        _flipVLabel.Text = _localization.T("L.EditFlipV");
        _cropButtonLabel.Text = _localization.T("L.EditCropEnter");
        _safeNote.Text = _localization.T("L.EditOriginalUnchanged");
        _cancelLabel.Text = _localization.T("L.EditCancel");
        _exportLabelControl.Text = _localization.T("L.EditExportCopy");
        _exportCardTitle.Text = _localization.T("L.EditExportCopy");
        _exportCardSub.Text = _localization.T("L.EditExportCard");
        _ratioButtons[0].Content = _localization.T("L.EditFree");
        _ratioButtons[1].Content = _localization.T("L.EditOriginalRatio");
        _ratioButtons[2].Content = "1:1";
        _ratioButtons[3].Content = "4:3";
        _ratioButtons[4].Content = "16:9";
    }

    public void SetCropSize(string text) => _cropSize.Text = text;

    public void SetActiveRatio(string ratio)
    {
        foreach (var button in _ratioButtons)
        {
            button.Classes.Set("active", string.Equals(button.CommandParameter as string, ratio, StringComparison.Ordinal));
        }
    }

    private TextBlock SectionLabel(out Button action, out Grid header, bool showAction = true)
    {
        var label = new TextBlock { Classes = { "edit-section-label" } };
        header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 14, 0, 9)
        };
        header.Children.Add(label);
        action = new Button
        {
            Content = _localization.T("L.EditReset"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.Parse("#D65A43")),
            IsVisible = showAction
        };
        Grid.SetColumn(action, 1);
        header.Children.Add(action);
        return label;
    }

    private Button ToolButton(string key, string iconKey, out TextBlock label)
    {
        var text = new TextBlock
        {
            Text = _localization.T(key),
            FontSize = 10,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new IconPath
        {
            Data = FindGeometry(iconKey),
            Width = 17,
            Height = 17,
            Stroke = new SolidColorBrush(Color.Parse("#69645C")),
            StrokeThickness = 1.65,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 3,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(icon);
        Grid.SetRow(text, 1);
        content.Children.Add(text);
        label = text;
        return new Button
        {
            Content = content,
            Classes = { "edit-tool" },
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private static Geometry FindGeometry(string key) =>
        Application.Current?.FindResource(key) as Geometry
        ?? StreamGeometry.Parse("M 0 0");

    private Button RatioButton(string ratio, out Button button)
    {
        button = new Button
        {
            Content = ratio,
            CommandParameter = ratio,
            Classes = { "edit-chip" }
        };
        button.Click += (_, _) => CropAspectRequested?.Invoke(this, ratio);
        return button;
    }

    private static TextBlock CenteredLabel(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
        Width = 128
    };

    private static Border Separator() => new()
    {
        BorderBrush = new SolidColorBrush(Color.Parse("#DED5CA")),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Margin = new Thickness(0, 4, 0, 0)
    };
}
