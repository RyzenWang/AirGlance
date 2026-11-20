using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AirGlance.App.Styles;

namespace AirGlance.App.Controls;

/// <summary>
/// A compact quality slider that matches the interaction design: a 4 px track,
/// a 14 px coral thumb and a coral fill. The stock Fluent slider has a much
/// thicker track and a larger thumb.
/// </summary>
internal sealed class CoralSlider : Grid
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CoralSlider, double>(nameof(Value), 90);

    private const double TrackHeight = 4;
    private const double ThumbSize = 14;
    private const double MinimumValue = 40;
    private const double MaximumValue = 100;

    private readonly Border _track;
    private readonly Border _fill;
    private readonly Border _thumb;
    private bool _dragging;

    public CoralSlider()
    {
        Height = 20;
        Background = Brushes.Transparent;
        Focusable = true;

        _track = new Border
        {
            Height = TrackHeight,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        _fill = new Border
        {
            Height = TrackHeight,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        _thumb = new Border
        {
            Width = ThumbSize,
            Height = ThumbSize,
            CornerRadius = new CornerRadius(ThumbSize / 2),
            BorderThickness = new Thickness(2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        ThemeResources.SetDynamic(_track, Border.BackgroundProperty, "Border");
        ThemeResources.SetDynamic(_fill, Border.BackgroundProperty, "Coral");
        ThemeResources.SetDynamic(_thumb, Border.BackgroundProperty, "Coral");
        ThemeResources.SetDynamic(_thumb, Border.BorderBrushProperty, "Surface");

        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_thumb);
        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        SizeChanged += (_, _) => UpdateVisual();
        UpdateVisual();
    }

    public event EventHandler? ValueChanged;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, MinimumValue, MaximumValue));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            UpdateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(this);
        SetFromPoint(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        SetFromPoint(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var next = e.Key switch
        {
            Key.Left => Value - 1,
            Key.Right => Value + 1,
            Key.Home => MinimumValue,
            Key.End => MaximumValue,
            _ => double.NaN
        };
        if (double.IsNaN(next))
        {
            return;
        }

        Value = next;
        e.Handled = true;
    }

    private void SetFromPoint(double x)
    {
        var available = Math.Max(1, Bounds.Width - ThumbSize);
        var ratio = Math.Clamp((x - ThumbSize / 2) / available, 0, 1);
        Value = MinimumValue + ratio * (MaximumValue - MinimumValue);
    }

    private void SetHover(bool hover)
    {
        if (hover)
        {
            _fill.Background = Res("AccentHover", Color.Parse("#FF8168"));
            _thumb.Background = Res("AccentHover", Color.Parse("#FF8168"));
            _track.Background = Res("BorderStrong", Color.Parse("#C7BEB0"));
            return;
        }

        ThemeResources.SetDynamic(_fill, Border.BackgroundProperty, "Coral");
        ThemeResources.SetDynamic(_thumb, Border.BackgroundProperty, "Coral");
        ThemeResources.SetDynamic(_track, Border.BackgroundProperty, "Border");
    }

    private static IBrush Res(string key, Color fallback) =>
        ThemeResources.Brush(key, fallback);

    private void UpdateVisual()
    {
        var available = Math.Max(0, Bounds.Width - ThumbSize);
        var ratio = (Value - MinimumValue) / (MaximumValue - MinimumValue);
        var center = ThumbSize / 2 + available * ratio;
        _fill.Width = Math.Max(0, center);
        _thumb.Margin = new Thickness(Math.Max(0, available * ratio), 0, 0, 0);
    }
}
