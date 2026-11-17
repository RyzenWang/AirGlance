using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AirGlance.App.Controls;

/// <summary>
/// Draws a 24-unit icon geometry scaled to the control's box and centred on it.
/// A plain <see cref="Avalonia.Controls.Shapes.Path"/> with <c>Stretch="None"</c> keeps
/// the geometry's own coordinates, so a glyph drawn between 4 and 20 sits off-centre in
/// an 18 px box and is clipped when the box is smaller than the artwork. Scaling the
/// whole design grid instead keeps every icon on the same optical centre, including the
/// stroke width, exactly like the 24 × 24 SVGs in the interaction prototype.
/// </summary>
public sealed class IconPath : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconPath, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<IconPath, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconPath, double>(nameof(StrokeThickness), 1.6);

    public static readonly StyledProperty<PenLineCap> StrokeLineCapProperty =
        AvaloniaProperty.Register<IconPath, PenLineCap>(nameof(StrokeLineCap), PenLineCap.Round);

    public static readonly StyledProperty<PenLineJoin> StrokeLineJoinProperty =
        AvaloniaProperty.Register<IconPath, PenLineJoin>(nameof(StrokeLineJoin), PenLineJoin.Round);

    public static readonly StyledProperty<double> DesignSizeProperty =
        AvaloniaProperty.Register<IconPath, double>(nameof(DesignSize), 24d);

    static IconPath()
    {
        AffectsRender<IconPath>(
            DataProperty,
            StrokeProperty,
            StrokeThicknessProperty,
            StrokeLineCapProperty,
            StrokeLineJoinProperty,
            DesignSizeProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public PenLineCap StrokeLineCap
    {
        get => GetValue(StrokeLineCapProperty);
        set => SetValue(StrokeLineCapProperty, value);
    }

    public PenLineJoin StrokeLineJoin
    {
        get => GetValue(StrokeLineJoinProperty);
        set => SetValue(StrokeLineJoinProperty, value);
    }

    public double DesignSize
    {
        get => GetValue(DesignSizeProperty);
        set => SetValue(DesignSizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var design = DesignSize > 0 ? DesignSize : 24;
        return new Size(design, design);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Stroke is not { } stroke)
        {
            return;
        }

        var design = DesignSize > 0 ? DesignSize : 24;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var scale = size / design;
        var offsetX = (Bounds.Width - design * scale) / 2;
        var offsetY = (Bounds.Height - design * scale) / 2;
        var pen = new Pen(
            stroke,
            StrokeThickness,
            lineCap: StrokeLineCap,
            lineJoin: StrokeLineJoin);

        using (context.PushTransform(
                   Matrix.CreateScale(scale, scale) *
                   Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(null, pen, data);
        }
    }
}
