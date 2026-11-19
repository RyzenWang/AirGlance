namespace AirGlance.Core.Imaging;

/// <summary>
/// A crop expressed in normalized coordinates (0..1) on the transformed image.
/// Keeping it normalized makes the same selection valid for the on-screen proxy and
/// for the full-resolution export.
/// </summary>
public readonly record struct CropRegion(double X, double Y, double Width, double Height)
{
    public static readonly CropRegion Full = new(0, 0, 1, 1);

    public bool IsFull =>
        X <= 0.0001 && Y <= 0.0001 && Width >= 0.9999 && Height >= 0.9999;

    public CropRegion Clamp()
    {
        var width = Math.Clamp(Width, 0.001, 1);
        var height = Math.Clamp(Height, 0.001, 1);
        var x = Math.Clamp(X, 0, 1 - width);
        var y = Math.Clamp(Y, 0, 1 - height);
        return new CropRegion(x, y, width, height);
    }

    /// <summary>Converts to integer pixels, always returning at least one pixel.</summary>
    public (int X, int Y, int Width, int Height) ToPixels(int imageWidth, int imageHeight)
    {
        var clamped = Clamp();
        var x = Math.Clamp((int)Math.Round(clamped.X * imageWidth), 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp((int)Math.Round(clamped.Y * imageHeight), 0, Math.Max(0, imageHeight - 1));
        var width = Math.Clamp((int)Math.Round(clamped.Width * imageWidth), 1, imageWidth - x);
        var height = Math.Clamp((int)Math.Round(clamped.Height * imageHeight), 1, imageHeight - y);
        return (x, y, width, height);
    }
}
