using NetVips;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Preview path for TIFFs too large for the full-raster LibTiff decoder. NetVips
/// asks libvips to reduce while reading, so a 40K image never needs a 3 GB managed
/// pixel buffer. This path intentionally only produces a reduced preview.
/// </summary>
internal static class NetVipsTiffDecoder
{
    public const int MaximumPreviewEdge = 8192;

    public static ImageMetadata ReadMetadata(string path)
    {
        var file = new FileInfo(path);
        using var image = Image.NewFromFile(path, access: Enums.Access.Sequential);
        return new ImageMetadata
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            FileSizeBytes = file.Length,
            LastWriteTime = file.LastWriteTimeUtc,
            RawWidth = image.Width,
            RawHeight = image.Height,
            DisplayWidth = image.Width,
            DisplayHeight = image.Height,
            Orientation = ImageOrientation.TopLeft,
            FormatName = "TIFF",
            FrameCount = 1,
            BitDepth = image.Format == Enums.BandFormat.Uchar ? 8 : 16,
            IccDescription = null
        };
    }

    public static DecodedBitmap DecodePreview(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = maxWidth > 0 ? Math.Min(maxWidth, MaximumPreviewEdge) : MaximumPreviewEdge;
            var height = maxHeight > 0 ? Math.Min(maxHeight, MaximumPreviewEdge) : MaximumPreviewEdge;

            using var image = Image.NewFromFile(path, access: Enums.Access.Sequential);
            using var thumb = Image.Thumbnail(
                path,
                width,
                height,
                size: Enums.Size.Down,
                noRotate: false);
            cancellationToken.ThrowIfCancellationRequested();

            var thumbWidth = thumb.Width;
            var thumbHeight = thumb.Height;
            var bands = thumb.Bands;
            var source = thumb.WriteToMemory<byte>();
            var required = checked(thumbWidth * thumbHeight * bands);
            if (source.Length < required)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The TIFF preview returned an incomplete pixel buffer.",
                    path,
                    Path.GetExtension(path));
            }

            var pixels = GC.AllocateUninitializedArray<byte>(checked(thumbWidth * thumbHeight * 4));
            var hasAlpha = bands is 2 or 4;
            for (var index = 0; index < thumbWidth * thumbHeight; index++)
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

            cancellationToken.ThrowIfCancellationRequested();
            return new DecodedBitmap(
                thumbWidth,
                thumbHeight,
                pixels,
                image.Width,
                image.Height,
                thumbWidth / (double)image.Width,
                isProxy: true)
            {
                HasAlpha = hasAlpha
            };
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DllNotFoundException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Unsupported,
                "The large TIFF preview decoder is not available on this platform.",
                path,
                Path.GetExtension(path),
                exception);
        }
        catch (Exception exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF is too large for the available preview decoder.",
                path,
                Path.GetExtension(path),
                exception);
        }
    }
}
