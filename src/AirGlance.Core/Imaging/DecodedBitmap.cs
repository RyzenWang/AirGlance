namespace AirGlance.Core.Imaging;

/// <summary>
/// Decoded pixels, already rotated to the EXIF orientation and converted to sRGB.
/// Pixels are 32-bit BGRA, premultiplied, matching Avalonia's <c>Bgra8888</c> format.
/// </summary>
public sealed class DecodedBitmap
{
    public DecodedBitmap(
        int width,
        int height,
        byte[] pixels,
        int sourceWidth,
        int sourceHeight,
        double scale,
        bool isProxy = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        if (pixels.Length < width * height * 4)
        {
            throw new ArgumentException("The pixel buffer is smaller than the image.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Stride = width * 4;
        Pixels = pixels;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Scale = scale <= 0 ? 1 : scale;
        IsProxy = isProxy;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public byte[] Pixels { get; }

    /// <summary>Width the pixels would have at 100% zoom, after EXIF orientation and edits.</summary>
    public int SourceWidth { get; }

    public int SourceHeight { get; }

    /// <summary>Decoded pixels per source pixel: 1.0 for a full-resolution decode.</summary>
    public double Scale { get; }

    public bool IsFullResolution => Scale >= 0.999;

    /// <summary>True when this bitmap is a reduced preview of a source too large to decode fully.</summary>
    public bool IsProxy { get; }

    public bool HasAlpha { get; set; } = true;

    public DecodedBitmap WithSourceSize(int sourceWidth, int sourceHeight) =>
        new(Width, Height, Pixels, sourceWidth, sourceHeight, Scale, IsProxy);
}
