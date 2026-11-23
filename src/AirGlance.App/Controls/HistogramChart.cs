using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace AirGlance.App.Controls;

public enum HistogramChartMode
{
    Luma,
    Rgb,
    Hue,
    Saturation
}

/// <summary>
/// A compact histogram renderer used by the analysis drawer. It draws 256-bin luma,
/// RGB overlay, or the 36/64-bin hue and saturation distributions and a hover readout
/// for the luma mode.
/// </summary>
public sealed class HistogramChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>?> LumaProperty =
        AvaloniaProperty.Register<HistogramChart, IReadOnlyList<int>?>(nameof(Luma));

    public static readonly StyledProperty<IReadOnlyList<int>?> RedProperty =
        AvaloniaProperty.Register<HistogramChart, IReadOnlyList<int>?>(nameof(Red));

    public static readonly StyledProperty<IReadOnlyList<int>?> GreenProperty =
        AvaloniaProperty.Register<HistogramChart, IReadOnlyList<int>?>(nameof(Green));

    public static readonly StyledProperty<IReadOnlyList<int>?> BlueProperty =
        AvaloniaProperty.Register<HistogramChart, IReadOnlyList<int>?>(nameof(Blue));

    public static readonly StyledProperty<IReadOnlyList<int>?> HueProperty =
        AvaloniaProperty.Register<HistogramChart, IReadOnlyList<int>?>(nameof(Hue));

    public static readonly StyledProperty<IReadOnlyList<int>?> SaturationProperty =
        AvaloniaProperty.Register<HistogramChart, IReadOnlyList<int>?>(nameof(Saturation));

    public static readonly StyledProperty<HistogramChartMode> ModeProperty =
        AvaloniaProperty.Register<HistogramChart, HistogramChartMode>(nameof(Mode));

    public static readonly StyledProperty<IBrush?> InkBrushProperty =
        AvaloniaProperty.Register<HistogramChart, IBrush?>(nameof(InkBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<HistogramChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> CoralBrushProperty =
        AvaloniaProperty.Register<HistogramChart, IBrush?>(nameof(CoralBrush));

    private int _hoverIndex = -1;

    static HistogramChart()
    {
        AffectsRender<HistogramChart>(
            LumaProperty,
            RedProperty,
            GreenProperty,
            BlueProperty,
            HueProperty,
            SaturationProperty,
            ModeProperty,
            InkBrushProperty,
            GridBrushProperty,
            CoralBrushProperty);
    }

    public IReadOnlyList<int>? Luma
    {
        get => GetValue(LumaProperty);
        set => SetValue(LumaProperty, value);
    }

    public IReadOnlyList<int>? Red
    {
        get => GetValue(RedProperty);
        set => SetValue(RedProperty, value);
    }

    public IReadOnlyList<int>? Green
    {
        get => GetValue(GreenProperty);
        set => SetValue(GreenProperty, value);
    }

    public IReadOnlyList<int>? Blue
    {
        get => GetValue(BlueProperty);
        set => SetValue(BlueProperty, value);
    }

    public IReadOnlyList<int>? Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public IReadOnlyList<int>? Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public HistogramChartMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IBrush? InkBrush
    {
        get => GetValue(InkBrushProperty);
        set => SetValue(InkBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? CoralBrush
    {
        get => GetValue(CoralBrushProperty);
        set => SetValue(CoralBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        using (context.PushClip(new Rect(0, 0, width, height)))
        {
            var grid = GridBrush ?? Brushes.Gray;
            var gridPen = new Pen(grid, 1);
            for (var i = 1; i <= 3; i++)
            {
                var x = width * i / 4.0;
                var y = height * i / 4.0;
                context.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
                context.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            }

            var baseline = new Pen(grid, 1);
            context.DrawLine(baseline, new Point(0, height - 0.5), new Point(width, height - 0.5));

            switch (Mode)
            {
                case HistogramChartMode.Luma:
                    DrawSingle(context, Luma, width, height, InkBrush ?? Brushes.Black);
                    break;
                case HistogramChartMode.Hue:
                    DrawSingle(context, Hue, width, height, InkBrush ?? Brushes.Black);
                    break;
                case HistogramChartMode.Saturation:
                    DrawSingle(context, Saturation, width, height, InkBrush ?? Brushes.Black);
                    break;
                case HistogramChartMode.Rgb:
                    DrawRgb(context, width, height);
                    break;
            }

            if (_hoverIndex >= 0 && Mode == HistogramChartMode.Luma && Luma is { Count: > 0 } luma)
            {
                DrawHover(context, luma, width, height);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (Mode != HistogramChartMode.Luma || Bounds.Width <= 1 || Luma is not { Count: > 0 } luma)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        var index = (int)(x / Bounds.Width * luma.Count);
        index = Math.Clamp(index, 0, luma.Count - 1);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverIndex >= 0)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    private void DrawSingle(
        DrawingContext context,
        IReadOnlyList<int>? values,
        double width,
        double height,
        IBrush brush)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        var max = Max(values);
        if (max <= 0)
        {
            return;
        }

        var geometry = BuildStepGeometry(values, width, height, max, filled: true);
        using (context.PushOpacity(0.74))
        {
            context.DrawGeometry(brush, null, geometry);
        }
    }

    private void DrawRgb(DrawingContext context, double width, double height)
    {
        var red = Red;
        var green = Green;
        var blue = Blue;
        if (red is not { Count: > 0 } || green is not { Count: > 0 } || blue is not { Count: > 0 })
        {
            return;
        }

        var max = Math.Max(Max(red), Math.Max(Max(green), Max(blue)));
        if (max <= 0)
        {
            return;
        }

        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var redBrush = new ImmutableSolidColorBrush(Color.Parse(dark ? "#FF8A70" : "#C9402A"));
        var greenBrush = new ImmutableSolidColorBrush(Color.Parse(dark ? "#7FE0AC" : "#2E6B4F"));
        var blueBrush = new ImmutableSolidColorBrush(Color.Parse(dark ? "#8FB2FF" : "#3B63C4"));

        DrawChannel(context, red, width, height, max, redBrush);
        DrawChannel(context, green, width, height, max, greenBrush);
        DrawChannel(context, blue, width, height, max, blueBrush);
    }

    private static void DrawChannel(
        DrawingContext context,
        IReadOnlyList<int> values,
        double width,
        double height,
        int max,
        IBrush brush)
    {
        var fill = BuildStepGeometry(values, width, height, max, filled: true);
        using (context.PushOpacity(0.24))
        {
            context.DrawGeometry(brush, null, fill);
        }

        var line = BuildStepGeometry(values, width, height, max, filled: false);
        context.DrawGeometry(null, new Pen(brush, 1.1), line);
    }

    private void DrawHover(
        DrawingContext context,
        IReadOnlyList<int> values,
        double width,
        double height)
    {
        var count = values.Count;
        var x = (Math.Clamp(_hoverIndex, 0, count - 1) + 0.5) * width / count;
        var coral = CoralBrush ?? Brushes.OrangeRed;
        context.DrawLine(
            new Pen(coral, 1.2, new DashStyle([2, 3], 0)),
            new Point(x, 0),
            new Point(x, height));

        var total = 0L;
        foreach (var value in values)
        {
            total += value;
        }

        var share = total == 0 ? 0 : values[Math.Clamp(_hoverIndex, 0, count - 1)] / (double)total;
        var text = new FormattedText(
            $"{_hoverIndex} · {share * 100:0.0}%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            Brushes.White);

        var left = Math.Min(width - text.Width - 16, Math.Max(4, x + 8));
        var rect = new Rect(left, 8, text.Width + 12, text.Height + 8);
        context.DrawRectangle(
            new ImmutableSolidColorBrush(Color.Parse("#CC161B21")),
            new Pen(new ImmutableSolidColorBrush(Color.Parse("#33FFFFFF")), 1),
            rect,
            4,
            4);
        context.DrawText(text, new Point(rect.X + 6, rect.Y + 4));
    }

    private static StreamGeometry BuildStepGeometry(
        IReadOnlyList<int> values,
        double width,
        double height,
        int max,
        bool filled)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        var step = width / values.Count;
        var baseline = height - 1;
        var top = 5.0;
        var firstY = baseline - values[0] / (double)max * (height - top);
        stream.BeginFigure(filled ? new Point(0, baseline) : new Point(0, firstY), filled);

        for (var i = 0; i < values.Count; i++)
        {
            var y = baseline - values[i] / (double)max * (height - top);
            var x0 = i * step;
            var x1 = (i + 1) * step;
            stream.LineTo(new Point(x0, y));
            stream.LineTo(new Point(x1, y));
        }

        if (filled)
        {
            stream.LineTo(new Point(width, baseline));
            stream.LineTo(new Point(0, baseline));
        }

        stream.EndFigure(filled);
        return geometry;
    }

    private static int Max(IReadOnlyList<int> values)
    {
        var max = 0;
        foreach (var value in values)
        {
            max = Math.Max(max, value);
        }

        return max;
    }
}
