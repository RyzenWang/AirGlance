using AirGlance.App.Services;
using AirGlance.App.Styles;
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
    private readonly TextBlock _cropSize;
    private readonly TextBlock _safeNote;
    private readonly Button _resetButton;
    private readonly Button _rotateLeft;
    private readonly Button _rotateRight;
    private readonly Button _flipH;
    private readonly Button _flipV;
    private readonly TextBlock _rotateLeftLabel;
    private readonly TextBlock _rotateRightLabel;
    private readonly TextBlock _flipHLabel;
    private readonly TextBlock _flipVLabel;
    private readonly Button _cropButton;
    private readonly TextBlock _cropButtonLabel;
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
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };

        _title = new TextBlock { Classes = { "edit-title" } };
        _subtitle = new TextBlock { Classes = { "edit-sub" }, Margin = new Thickness(0, 5, 0, 0) };
        var eyebrow = new TextBlock { Text = "NON-DESTRUCTIVE", Classes = { "edit-eyebrow" } };
        var closeIcon = new IconPath
        {
            Data = FindGeometry("IconClose"),
            Width = 15,
            Height = 15,
            StrokeThickness = 1.7
        };
        ThemeResources.SetDynamic(closeIcon, IconPath.StrokeProperty, "InkSecondary");
        var close = new Button
        {
            Content = closeIcon,
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

        _cropLabel = SectionLabel(out _, out var cropHeader, showAction: false);
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

        _cropSize = new TextBlock
        {
            Classes = { "caption" },
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 0)
        };
        ThemeResources.SetDynamic(_cropSize, TextBlock.ForegroundProperty, "InkMuted");
        body.Children.Add(_cropSize);
        body.Children.Add(Separator());


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
        ThemeResources.SetDynamic(_safeNote, TextBlock.ForegroundProperty, "Teal");
        var safeIcon = new IconPath
        {
            Data = FindGeometry("IconShieldCheck"),
            Width = 14,
            Height = 14,
            StrokeThickness = 1.7,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeResources.SetDynamic(safeIcon, IconPath.StrokeProperty, "Teal");
        var safeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 9),
            Children =
            {
                safeIcon,
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
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 82, 22, 0)
        };
        panelBorder.Classes.Add("edit-panel");
        Content = panelBorder;
        ApplyLanguage();
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler? CropRequested;
    public event EventHandler<EditAction>? TransformRequested;

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
        _resetButton.Content = _localization.T("L.EditReset");
        _rotateLeftLabel.Text = _localization.T("L.EditRotateLeft");
        _rotateRightLabel.Text = _localization.T("L.EditRotateRight");
        _flipHLabel.Text = _localization.T("L.EditFlipH");
        _flipVLabel.Text = _localization.T("L.EditFlipV");
        _cropButtonLabel.Text = _localization.T("L.EditCropEnter");
        _safeNote.Text = _localization.T("L.EditOriginalUnchanged");
        _cancelLabel.Text = _localization.T("L.EditCancel");
        _exportLabelControl.Text = _localization.T("L.EditExportCopy");
    }

    public void SetCropSize(string text) => _cropSize.Text = text;

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
            IsVisible = showAction
        };
        ThemeResources.SetDynamic(action, Button.ForegroundProperty, "Coral");
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
        ThemeResources.SetDynamic(text, TextBlock.ForegroundProperty, "Ink");
        var icon = new IconPath
        {
            Data = FindGeometry(iconKey),
            Width = 17,
            Height = 17,
            StrokeThickness = 1.65,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeResources.SetDynamic(icon, IconPath.StrokeProperty, "InkSecondary");
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

    private static Border Separator()
    {
        var border = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 4, 0, 0)
        };
        ThemeResources.SetDynamic(border, Border.BorderBrushProperty, "Border");
        return border;
    }
}
