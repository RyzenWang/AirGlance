using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace AirGlance.App.Controls;

/// <summary>A 0-255 tone ramp with a marker at the median.</summary>
public sealed class ToneBar : Control
{
    private static readonly IBrush Ramp = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#111418"), 0),
            new GradientStop(Color.Parse("#6E6A64"), 0.5),
            new GradientStop(Color.Parse("#F3EFE6"), 1)
        }
    };

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ToneBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> MarkerProperty =
        AvaloniaProperty.Register<ToneBar, IBrush?>(nameof(Marker));

    static ToneBar()
    {
        AffectsRender<ToneBar>(ValueProperty, MarkerProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Marker
    {
        get => GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Math.Max(2, Bounds.Height);
        if (width <= 0)
        {
            return;
        }

        context.DrawRectangle(
            Ramp,
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#33202424")), 1),
            new Rect(0, 0, width, height),
            2,
            2);

        var x = Math.Clamp(Value, 0, 1) * width;
        var marker = new Rect(x - 1, -3, 2, height + 6);
        context.DrawRectangle(
            Marker ?? Brushes.OrangeRed,
            new Pen(Brushes.White, 1),
            marker,
            1,
            1);
    }
}
