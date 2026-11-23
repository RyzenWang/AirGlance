namespace AirGlance.Core.Imaging;

/// <summary>An 8-bit sRGB colour, independent of the UI framework.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Rec. 709 luminance, 0 to 1, on the encoded sRGB values.</summary>
    public double Luminance => (0.2126 * R + 0.7152 * G + 0.0722 * B) / 255.0;
}

public readonly record struct HslColor(double H, double S, double L);

public readonly record struct LabColor(double L, double A, double B);

public static class ColorMath
{
    public static HslColor RgbToHsl(RgbColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 0.000001)
        {
            return new HslColor(0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        return new HslColor(h * 60, s, l);
    }

    public static LabColor RgbToLab(RgbColor color)
    {
        var r = ToLinear(color.R / 255.0);
        var g = ToLinear(color.G / 255.0);
        var b = ToLinear(color.B / 255.0);

        var x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;

        var fx = LabF(x / 0.95047);
        var fy = LabF(y);
        var fz = LabF(z / 1.08883);

        return new LabColor(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double ToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double LabF(double value) =>
        value > 0.008856 ? Math.Cbrt(value) : 7.787 * value + 16.0 / 116.0;
}
