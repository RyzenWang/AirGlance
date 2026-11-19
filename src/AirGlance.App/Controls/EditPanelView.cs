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
    private readonly Button[] _ratioButtons;
    private readonly CheckBox _cropOverlay;
    private readonly Button _exportCard;
    private readonly Button _cancel;
    private readonly Button _export;
    private bool _syncing;

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
            Content = "×",
            Classes = { "icon" },
            FontSize = 19,
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

        _rotateLeft = ToolButton("L.EditRotateLeft", out var rotateLeftText);
        _rotateLeft.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.Left);
        _rotateLeft.Content = rotateLeftText;
        _rotateRight = ToolButton("L.EditRotateRight", out var rotateRightText);
        _rotateRight.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.Right);
        _rotateRight.Content = rotateRightText;
        _flipH = ToolButton("L.EditFlipH", out var flipHText);
        _flipH.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.FlipH);
        _flipH.Content = flipHText;
        _flipV = ToolButton("L.EditFlipV", out var flipVText);
        _flipV.Click += (_, _) => TransformRequested?.Invoke(this, EditAction.FlipV);
        _flipV.Content = flipVText;
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

        _cropOverlay = new CheckBox
        {
            Content = _localization.T("L.EditCropOverlay"),
            IsChecked = true,
            Margin = new Thickness(0, 9, 0, 0)
        };
        _cropOverlay.Click += (_, _) =>
        {
            if (!_syncing)
            {
                CropOverlayChanged?.Invoke(this, _cropOverlay.IsChecked == true);
            }
        };
        body.Children.Add(_cropOverlay);
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
        _exportCard = new Button
        {
            Classes = { "edit-card" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                Children =
                {
                    new Border
                    {
                        Width = 34,
                        Height = 34,
                        CornerRadius = new CornerRadius(9),
                        Background = new SolidColorBrush(Color.Parse("#FBE9E2")),
                        Child = new TextBlock
                        {
                            Text = "↓",
                            FontSize = 17,
                            Foreground = new SolidColorBrush(Color.Parse("#D65A43")),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    new StackPanel
                    {
                        Children = { _exportCardTitle, _exportCardSub },
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "›",
                        FontSize = 17,
                        Foreground = new SolidColorBrush(Color.Parse("#948A7E")),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
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
            Text = "✓  " + _localization.T("L.EditOriginalUnchanged"),
            Classes = { "caption" },
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 0, 9)
        };
        _cancel = new Button
        {
            Content = _localization.T("L.EditCancel"),
            Classes = { "edit-action" }
        };
        _cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _export = new Button
        {
            Content = _localization.T("L.EditExportCopy"),
            Classes = { "edit-primary" }
        };
        _export.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,1.35*"),
            ColumnSpacing = 8
        };
        actions.Children.Add(_cancel);
        Grid.SetColumn(_export, 1);
        actions.Children.Add(_export);
        var footer = new Border
        {
            Classes = { "edit-footer" },
            Child = new StackPanel { Children = { _safeNote, actions } }
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
    public event EventHandler<EditAction>? TransformRequested;
    public event EventHandler<string>? CropAspectRequested;
    public event EventHandler<bool>? CropOverlayChanged;

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
        _rotateLeft.Content = _localization.T("L.EditRotateLeft");
        _rotateRight.Content = _localization.T("L.EditRotateRight");
        _flipH.Content = _localization.T("L.EditFlipH");
        _flipV.Content = _localization.T("L.EditFlipV");
        _cropOverlay.Content = _localization.T("L.EditCropOverlay");
        _safeNote.Text = "✓  " + _localization.T("L.EditOriginalUnchanged");
        _cancel.Content = _localization.T("L.EditCancel");
        _export.Content = _localization.T("L.EditExportCopy");
        _exportCardTitle.Text = _localization.T("L.EditExportCopy");
        _exportCardSub.Text = _localization.T("L.EditExportCard");
        _ratioButtons[0].Content = _localization.T("L.EditFree");
        _ratioButtons[1].Content = _localization.T("L.EditOriginalRatio");
        _ratioButtons[2].Content = "1:1";
        _ratioButtons[3].Content = "4:3";
        _ratioButtons[4].Content = "16:9";
    }

    public void SetCropSize(string text) => _cropSize.Text = text;

    public void SetCropOverlay(bool active)
    {
        _syncing = true;
        _cropOverlay.IsChecked = active;
        _syncing = false;
    }

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

    private Button ToolButton(string key, out TextBlock text)
    {
        text = new TextBlock
        {
            Text = _localization.T(key),
            FontSize = 10,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        return new Button
        {
            Content = text,
            Classes = { "edit-tool" },
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

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

    private static Border Separator() => new()
    {
        BorderBrush = new SolidColorBrush(Color.Parse("#DED5CA")),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Margin = new Thickness(0, 4, 0, 0)
    };
}
