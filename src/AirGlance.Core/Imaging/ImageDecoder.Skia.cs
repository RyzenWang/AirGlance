using AirGlance.Core.Files;
using BitMiracle.LibTiff.Classic;
using System.Runtime.InteropServices;
using SkiaSharp;
using Svg.Skia;
using TiffOrientation = BitMiracle.LibTiff.Classic.Orientation;

namespace AirGlance.Core.Imaging;

public static partial class ImageDecoder
{
    public static DecodedBitmap Decode(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default)
    {
        Validate(path);
        cancellationToken.ThrowIfCancellationRequested();

        if (SupportedFormats.IsTiff(path))
        {
            return DecodeTiff(path, maxWidth, maxHeight, cancellationToken);
        }

        if (SupportedFormats.IsSvg(path))
        {
            return DecodeSvg(path, maxWidth, maxHeight, cancellationToken);
        }

        try
        {
            if (SupportedFormats.IsAvif(path))
            {
                var avif = LibAvifDecoder.Decode(path);
                cancellationToken.ThrowIfCancellationRequested();

                if (maxWidth <= 0 || maxHeight <= 0)
                {
                    return avif;
                }

                var avifScale = Math.Min(
                    1.0,
                    Math.Min((double)maxWidth / avif.SourceWidth, (double)maxHeight / avif.SourceHeight));
                if (avifScale >= 0.99)
                {
                    return avif;
                }

                var avifWidth = Math.Max(1, (int)Math.Round(avif.SourceWidth * avifScale));
                var avifHeight = Math.Max(1, (int)Math.Round(avif.SourceHeight * avifScale));
                var avifPixels = Downscale(avif.Pixels, avif.Width, avif.Height, avifWidth, avifHeight);
                return new DecodedBitmap(
                    avifWidth,
                    avifHeight,
                    avifPixels,
                    avif.SourceWidth,
                    avif.SourceHeight,
                    avifWidth / (double)avif.SourceWidth)
                {
                    HasAlpha = avif.HasAlpha
                };
            }

            using var stream = OpenRead(path);
            using var codec = SKCodec.Create(stream)
                ?? throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "SkiaSharp could not read the image.",
                    path,
                    Path.GetExtension(path));

            var orientation = GetOrientation(codec);
            if (orientation == ImageOrientation.TopLeft)
            {
                // Skia reports no encoded origin for PNG (and some other codecs), so a
                // stored eXIf orientation would otherwise leave the image sideways. The
                // managed reader has already parsed it; use it only as a fallback.
                orientation = ManagedOrientation(path);
            }
            var (orientedWidth, orientedHeight) = orientation.OrientedSize(codec.Info.Width, codec.Info.Height);
            var fullResolution = maxWidth <= 0 || maxHeight <= 0;
            var targetScale = 1.0;

            if (!fullResolution)
            {
                targetScale = Math.Min(
                    1.0,
                    Math.Min((double)maxWidth / orientedWidth, (double)maxHeight / orientedHeight));
                targetScale = Math.Max(targetScale, 1.0 / 64.0);
            }

            var scaled = codec.GetScaledDimensions((float)targetScale);
            scaled = NormalizeScaled(scaled, codec.Info);

            if (!fullResolution && targetScale < 1)
            {
                scaled = BumpToUsefulScale(codec, scaled, orientation, targetScale, orientedWidth, orientedHeight);
            }

            var pixelCount = (long)scaled.Width * scaled.Height;
            if (pixelCount <= 0 || pixelCount > int.MaxValue / 4)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.TooLarge,
                    "The decoded image would be too large.",
                    path,
                    Path.GetExtension(path));
            }

            // Codecs that cannot scale down (PNG, WebP, BMP) would allocate the whole
            // image just to shrink it, so refuse the ones no screen can use instead of
            // risking an out-of-memory crash on the decode.
            if (!fullResolution && pixelCount > PreviewPixelBudget)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.TooLarge,
                    "The image is too large to decode for a preview.",
                    path,
                    Path.GetExtension(path));
            }

            var info = new SKImageInfo(
                scaled.Width,
                scaled.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgb());

            var pixels = GC.AllocateUninitializedArray<byte>(checked(scaled.Width * scaled.Height * 4));
            SKCodecResult result;

            unsafe
            {
                fixed (byte* destination = pixels)
                {
                    result = codec.GetPixels(info, (IntPtr)destination);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (result != SKCodecResult.Success)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    $"The image could not be decoded ({result}).",
                    path,
                    Path.GetExtension(path));
            }

            // Some codecs (notably PNG) ignore the requested scale and return the
            // full image. Downsample in software so a large PNG preview does not keep
            // hundreds of megabytes alive just to show a fit-window view.
            if (!fullResolution &&
                targetScale < 0.99 &&
                scaled.Width == codec.Info.Width &&
                scaled.Height == codec.Info.Height &&
                (scaled.Width > maxWidth || scaled.Height > maxHeight))
            {
                var targetWidth = Math.Max(1, (int)Math.Round(scaled.Width * targetScale));
                var targetHeight = Math.Max(1, (int)Math.Round(scaled.Height * targetScale));
                pixels = Downscale(pixels, scaled.Width, scaled.Height, targetWidth, targetHeight);
                scaled = new SKSizeI(targetWidth, targetHeight);
            }

            var decodedWidth = orientation.SwapsAxes() ? scaled.Height : scaled.Width;
            var scale = decodedWidth / (double)orientedWidth;
            var decoded = new DecodedBitmap(
                scaled.Width,
                scaled.Height,
                pixels,
                orientedWidth,
                orientedHeight,
                scale)
            {
                HasAlpha = FormatMayHaveAlpha(codec.EncodedFormat)
            };

            return ImageTransform.ApplyOrientation(decoded, orientation, orientedWidth, orientedHeight);
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutOfMemoryException exception)
        {
            // A codec can still exhaust memory inside the native decoder even when the
            // pixel budget above is satisfied; report it instead of letting it escape.
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The image could not be decoded because there was not enough memory.",
                path,
                Path.GetExtension(path),
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.AccessDenied,
                "The file could not be read because access was denied.",
                path,
                Path.GetExtension(path),
                exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.NotFound,
                "The file does not exist.",
                path,
                Path.GetExtension(path),
                exception);
        }
        catch (IOException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Unknown,
                "The file could not be read.",
                path,
                Path.GetExtension(path),
                exception);
        }
    }

    public static DecodedBitmap DecodeFull(string path, CancellationToken cancellationToken = default) =>
        Decode(path, 0, 0, cancellationToken);

    private static void ClearRect(byte[] canvas, int width, SKRectI rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var bytesPerRow = rect.Width * 4;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            var offset = (y * width + rect.Left) * 4;
            Array.Clear(canvas, offset, bytesPerRow);
        }
    }

    private static void CopyFrame(
        byte[] canvas,
        byte[] frame,
        int canvasWidth,
        int canvasHeight,
        SKRectI rect,
        SKCodecAnimationBlend blend)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var frameWidth = canvasWidth;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            var row = y * frameWidth;
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var sourceOffset = (row + x) * 4;
                var destinationOffset = sourceOffset;
                var sourceAlpha = frame[sourceOffset + 3];
                if (blend == SKCodecAnimationBlend.Src)
                {
                    canvas[destinationOffset] = frame[sourceOffset];
                    canvas[destinationOffset + 1] = frame[sourceOffset + 1];
                    canvas[destinationOffset + 2] = frame[sourceOffset + 2];
                    canvas[destinationOffset + 3] = sourceAlpha;
                }
                else if (sourceAlpha == 255)
                {
                    canvas[destinationOffset] = frame[sourceOffset];
                    canvas[destinationOffset + 1] = frame[sourceOffset + 1];
                    canvas[destinationOffset + 2] = frame[sourceOffset + 2];
                    canvas[destinationOffset + 3] = 255;
                }
                else if (sourceAlpha > 0)
                {
                    var inverse = 255 - sourceAlpha;
                    canvas[destinationOffset] = (byte)(frame[sourceOffset] + canvas[destinationOffset] * inverse / 255);
                    canvas[destinationOffset + 1] = (byte)(frame[sourceOffset + 1] + canvas[destinationOffset + 1] * inverse / 255);
                    canvas[destinationOffset + 2] = (byte)(frame[sourceOffset + 2] + canvas[destinationOffset + 2] * inverse / 255);
                    canvas[destinationOffset + 3] = (byte)(sourceAlpha + canvas[destinationOffset + 3] * inverse / 255);
                }
            }
        }
    }

    private static SKRectI ScaleRect(SKRectI rect, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return SKRectI.Empty;
        }

        var left = Math.Clamp((int)Math.Floor(rect.Left * (double)targetWidth / sourceWidth), 0, targetWidth);
        var top = Math.Clamp((int)Math.Floor(rect.Top * (double)targetHeight / sourceHeight), 0, targetHeight);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right * (double)targetWidth / sourceWidth), 0, targetWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom * (double)targetHeight / sourceHeight), 0, targetHeight);
        return right > left && bottom > top
            ? new SKRectI(left, top, right, bottom)
            : SKRectI.Empty;
    }

    private static byte[] Downscale(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var sourcePixels = MemoryMarshal.Cast<byte, uint>(source.AsSpan());
        var target = GC.AllocateUninitializedArray<byte>(targetWidth * targetHeight * 4);
        var targetPixels = MemoryMarshal.Cast<byte, uint>(target.AsSpan());

        // Every source pixel the target pixel covers, per axis. Precomputing the spans
        // keeps the division out of the inner loop and lets the result be an average
        // instead of a point sample, which is what caused moiré on large reductions.
        var columns = new (int Start, int Count)[targetWidth];
        for (var x = 0; x < targetWidth; x++)
        {
            var start = (int)((long)x * sourceWidth / targetWidth);
            var end = (int)((long)(x + 1) * sourceWidth / targetWidth);
            columns[x] = (start, Math.Max(1, end - start));
        }

        for (var y = 0; y < targetHeight; y++)
        {
            var rowStart = (int)((long)y * sourceHeight / targetHeight);
            var rowEnd = (int)((long)(y + 1) * sourceHeight / targetHeight);
            var rowCount = Math.Max(1, rowEnd - rowStart);
            var targetRow = y * targetWidth;

            for (var x = 0; x < targetWidth; x++)
            {
                var (columnStart, columnCount) = columns[x];
                uint a = 0, r = 0, g = 0, b = 0;
                for (var sy = rowStart; sy < rowStart + rowCount; sy++)
                {
                    var sourceRow = sy * sourceWidth;
                    for (var sx = columnStart; sx < columnStart + columnCount; sx++)
                    {
                        var pixel = sourcePixels[sourceRow + sx];
                        a += pixel >> 24;
                        r += (pixel >> 16) & 0xFF;
                        g += (pixel >> 8) & 0xFF;
                        b += pixel & 0xFF;
                    }
                }

                var count = (uint)(rowCount * columnCount);
                var half = count / 2;
                targetPixels[targetRow + x] =
                    (((a + half) / count) << 24) |
                    (((r + half) / count) << 16) |
                    (((g + half) / count) << 8) |
                    ((b + half) / count);
            }
        }

        return target;
    }

    private static ImageOrientation GetOrientation(SKCodec codec)
    {
        var origin = (int)codec.EncodedOrigin;
        return origin is >= 1 and <= 8 ? (ImageOrientation)origin : ImageOrientation.TopLeft;
    }

    private static SKSizeI NormalizeScaled(SKSizeI scaled, SKImageInfo info)
    {
        var width = Math.Clamp(scaled.Width, 1, info.Width);
        var height = Math.Clamp(scaled.Height, 1, info.Height);
        return new SKSizeI(width, height);
    }

    private static bool FormatMayHaveAlpha(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => false,
        SKEncodedImageFormat.Gif => true,
        SKEncodedImageFormat.Png => true,
        SKEncodedImageFormat.Webp => true,
        SKEncodedImageFormat.Ico => true,
        SKEncodedImageFormat.Bmp => true,
        _ => true
    };

    private static string DescribeFormat(SKEncodedImageFormat format, string path) => format switch
    {
        SKEncodedImageFormat.Jpeg => "JPEG",
        SKEncodedImageFormat.Png => "PNG",
        SKEncodedImageFormat.Webp => "WebP",
        SKEncodedImageFormat.Bmp => "BMP",
        SKEncodedImageFormat.Gif => "GIF",
        SKEncodedImageFormat.Ico => "ICO",
        SKEncodedImageFormat.Wbmp => "WBMP",
        _ => SupportedFormats.Describe(path)
    };
}
