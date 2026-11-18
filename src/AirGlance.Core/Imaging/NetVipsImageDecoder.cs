using NetVips;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Still-image decoding through libvips for formats SkiaSharp does not handle,
/// currently AVIF. AVIF images are normal-sized enough to decode fully into the
/// existing managed RGBA buffer.
/// </summary>
internal static class NetVipsImageDecoder
{
    public static ImageMetadata ReadMetadata(string path)
    {
        var file = new FileInfo(path);
        using var image = Image.NewFromFile(path, access: Enums.Access.Sequential);
        using var oriented = image.Autorot();
        return new ImageMetadata
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            FileSizeBytes = file.Length,
            LastWriteTime = file.LastWriteTimeUtc,
            RawWidth = image.Width,
            RawHeight = image.Height,
            DisplayWidth = oriented.Width,
            DisplayHeight = oriented.Height,
            Orientation = ImageOrientation.TopLeft,
            FormatName = "AVIF",
            FrameCount = 1,
            BitDepth = oriented.Format == Enums.BandFormat.Uchar ? 8 : 16,
            IccDescription = null
        };
    }

    public static DecodedBitmap Decode(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var image = Image.NewFromFile(path, access: Enums.Access.Sequential);
        using var oriented = image.Autorot();
        var sourceWidth = oriented.Width;
        var sourceHeight = oriented.Height;
        var scale = maxWidth <= 0 || maxHeight <= 0
            ? 1.0
            : Math.Min(1.0, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));

        using var resized = scale < 0.999
            ? oriented.Resize(scale, kernel: Enums.Kernel.Lanczos3)
            : null;
        var output = resized ?? oriented;
        cancellationToken.ThrowIfCancellationRequested();

        var width = output.Width;
        var height = output.Height;
        var bands = output.Bands;
        var source = output.WriteToMemory<byte>();
        var required = checked(width * height * bands);
        if (source.Length < required)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The AVIF decoder returned an incomplete pixel buffer.",
                path,
                Path.GetExtension(path));
        }

        var pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        var hasAlpha = bands is 2 or 4;
        for (var index = 0; index < width * height; index++)
        {
            byte red;
            byte green;
            byte blue;
            byte alpha = 255;

            if (bands == 1)
            {
                red = green = blue = source[index];
            }
            else if (bands == 2)
            {
                red = green = blue = source[index * 2];
                alpha = source[index * 2 + 1];
            }
            else if (bands == 3)
            {
                var offset = index * 3;
                red = source[offset];
                green = source[offset + 1];
                blue = source[offset + 2];
            }
            else
            {
                var offset = index * 4;
                red = source[offset];
                green = source[offset + 1];
                blue = source[offset + 2];
                alpha = source[offset + 3];
            }

            if (alpha != 255)
            {
                red = (byte)(red * alpha / 255);
                green = (byte)(green * alpha / 255);
                blue = (byte)(blue * alpha / 255);
            }

            var target = index * 4;
            pixels[target] = blue;
            pixels[target + 1] = green;
            pixels[target + 2] = red;
            pixels[target + 3] = alpha;
        }

        return new DecodedBitmap(
            width,
            height,
            pixels,
            sourceWidth,
            sourceHeight,
            width / (double)sourceWidth)
        {
            HasAlpha = hasAlpha
        };
    }
}
