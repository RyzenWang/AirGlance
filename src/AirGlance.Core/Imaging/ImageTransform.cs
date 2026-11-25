using System.Runtime.InteropServices;

namespace AirGlance.Core.Imaging;

/// <summary>
/// A display-only edit: mirror first, then rotate clockwise. Keeping one canonical form
/// means any sequence of R, Shift+R, H and V always collapses to eight states.
/// </summary>
public readonly record struct EditTransform(int QuarterTurns, bool MirrorHorizontal)
{
    public static readonly EditTransform None = new(0, false);

    public bool IsIdentity => QuarterTurns == 0 && !MirrorHorizontal;

    public bool SwapsAxes => (QuarterTurns & 1) == 1;

    public EditTransform RotateClockwise() => this with { QuarterTurns = (QuarterTurns + 1) & 3 };

    public EditTransform RotateCounterClockwise() => this with { QuarterTurns = (QuarterTurns + 3) & 3 };

    public EditTransform FlipHorizontal() =>
        new((4 - QuarterTurns) & 3, !MirrorHorizontal);

    public EditTransform FlipVertical() =>
        new((2 - QuarterTurns) & 3, !MirrorHorizontal);
}

/// <summary>Pixel transformations used for EXIF orientation and the rotate/flip keys.</summary>
public static class ImageTransform
{
    public static DecodedBitmap ApplyOrientation(
        DecodedBitmap decoded,
        ImageOrientation orientation,
        int sourceWidth,
        int sourceHeight)
    {
        if (orientation == ImageOrientation.TopLeft)
        {
            return decoded;
        }

        var (pixels, width, height) = MapOrientation(
            decoded.Pixels,
            decoded.Width,
            decoded.Height,
            orientation);

        return new DecodedBitmap(
            width,
            height,
            pixels,
            sourceWidth,
            sourceHeight,
            width / (double)sourceWidth,
            decoded.IsProxy)
        {
            HasAlpha = decoded.HasAlpha
        };
    }

    public static DecodedBitmap Apply(DecodedBitmap decoded, EditTransform transform)
    {
        if (transform.IsIdentity)
        {
            return decoded;
        }

        var working = transform.MirrorHorizontal ? MirrorHorizontal(decoded) : decoded;
        var quarterTurns = transform.QuarterTurns & 3;

        if (quarterTurns == 0)
        {
            return working;
        }

        return Rotate(working, quarterTurns);
    }

    public static DecodedBitmap MirrorHorizontal(DecodedBitmap decoded)
    {
        var width = decoded.Width;
        var height = decoded.Height;
        var source = MemoryMarshal.Cast<byte, uint>(decoded.Pixels.AsSpan());
        var destinationBytes = GC.AllocateUninitializedArray<byte>(width * height * 4);
        var destination = MemoryMarshal.Cast<byte, uint>(destinationBytes.AsSpan());

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                destination[row + x] = source[row + (width - 1 - x)];
            }
        }

        return new DecodedBitmap(
            width,
            height,
            destinationBytes,
            decoded.SourceWidth,
            decoded.SourceHeight,
            decoded.Scale)
        {
            HasAlpha = decoded.HasAlpha
        };
    }

    public static DecodedBitmap RotateClockwise(DecodedBitmap decoded)
    {
        var width = decoded.Width;
        var height = decoded.Height;
        var source = MemoryMarshal.Cast<byte, uint>(decoded.Pixels.AsSpan());
        var destinationBytes = GC.AllocateUninitializedArray<byte>(width * height * 4);
        var destination = MemoryMarshal.Cast<byte, uint>(destinationBytes.AsSpan());
        var destinationWidth = height;
        var destinationHeight = width;

        for (var y = 0; y < destinationHeight; y++)
        {
            var row = y * destinationWidth;
            for (var x = 0; x < destinationWidth; x++)
            {
                destination[row + x] = source[(height - 1 - x) * width + y];
            }
        }

        return new DecodedBitmap(
            destinationWidth,
            destinationHeight,
            destinationBytes,
            decoded.SourceHeight,
            decoded.SourceWidth,
            decoded.Scale)
        {
            HasAlpha = decoded.HasAlpha
        };
    }

    public static (byte[] Pixels, int Width, int Height) MapOrientation(
        byte[] sourcePixels,
        int width,
        int height,
        ImageOrientation orientation)
    {
        var (destinationWidth, destinationHeight) = orientation.OrientedSize(width, height);
        var source = MemoryMarshal.Cast<byte, uint>(sourcePixels.AsSpan());
        var destinationBytes = GC.AllocateUninitializedArray<byte>(destinationWidth * destinationHeight * 4);
        var destination = MemoryMarshal.Cast<byte, uint>(destinationBytes.AsSpan());
        var orientationValue = (int)orientation.Normalize();

        for (var y = 0; y < destinationHeight; y++)
        {
            var row = y * destinationWidth;
            for (var x = 0; x < destinationWidth; x++)
            {
                var (sourceX, sourceY) = orientationValue switch
                {
                    1 => (x, y),
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (y, height - 1 - x),
                    7 => (width - 1 - y, height - 1 - x),
                    8 => (width - 1 - y, x),
                    _ => (x, y)
                };

                destination[row + x] = source[sourceY * width + sourceX];
            }
        }

        return (destinationBytes, destinationWidth, destinationHeight);
    }

    private static DecodedBitmap Rotate(DecodedBitmap decoded, int quarterTurns)
    {
        var width = decoded.Width;
        var height = decoded.Height;
        var source = MemoryMarshal.Cast<byte, uint>(decoded.Pixels.AsSpan());
        var swaps = (quarterTurns & 1) == 1;
        var destinationWidth = swaps ? height : width;
        var destinationHeight = swaps ? width : height;
        var destinationBytes = GC.AllocateUninitializedArray<byte>(destinationWidth * destinationHeight * 4);
        var destination = MemoryMarshal.Cast<byte, uint>(destinationBytes.AsSpan());

        for (var y = 0; y < destinationHeight; y++)
        {
            var row = y * destinationWidth;
            for (var x = 0; x < destinationWidth; x++)
            {
                var (sourceX, sourceY) = quarterTurns switch
                {
                    1 => (y, height - 1 - x),
                    2 => (width - 1 - x, height - 1 - y),
                    3 => (width - 1 - y, x),
                    _ => (x, y)
                };

                destination[row + x] = source[sourceY * width + sourceX];
            }
        }

        return new DecodedBitmap(
            destinationWidth,
            destinationHeight,
            destinationBytes,
            swaps ? decoded.SourceHeight : decoded.SourceWidth,
            swaps ? decoded.SourceWidth : decoded.SourceHeight,
            decoded.Scale)
        {
            HasAlpha = decoded.HasAlpha
        };
    }
}
