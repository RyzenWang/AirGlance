using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AirGlance.App.Controls;

/// <summary>A thin horizontal meter used by the quality cards.</summary>
public sealed class MeterBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MeterBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<MeterBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<MeterBar, IBrush?>(nameof(Track));

    static MeterBar()
    {
        AffectsRender<MeterBar>(ValueProperty, FillProperty, TrackProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
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

        var radius = height / 2;
        context.DrawRectangle(
            Track ?? Brushes.LightGray,
            null,
            new Rect(0, 0, width, height),
            radius,
            radius);

        var fraction = Math.Clamp(Value, 0, 1);
        if (fraction <= 0)
        {
            return;
        }

        context.DrawRectangle(
            Fill ?? Brushes.DodgerBlue,
            null,
            new Rect(0, 0, width * fraction, height),
            radius,
            radius);
    }
}
