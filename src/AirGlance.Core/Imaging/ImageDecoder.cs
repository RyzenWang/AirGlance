using AirGlance.Core.Files;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Decodes images off the UI thread: EXIF orientation is applied, the embedded ICC
/// profile is converted to sRGB, and downscaled decoding is requested whenever the
/// caller only needs a screen-sized image.
/// </summary>
public static class ImageDecoder
{
    public static DecodedBitmap Decode(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default)
    {
        Validate(path);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var stream = OpenRead(path);
            using var codec = SKCodec.Create(stream)
                ?? throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "SkiaSharp could not read the image.",
                    path,
                    Path.GetExtension(path));

            var orientation = GetOrientation(codec);
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
                    ImageLoadFailure.Corrupt,
                    "The decoded image would be too large.",
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

    /// <summary>
    /// Decodes at full resolution into unmanaged memory. This is the tile-cache entry
    /// point; the caller must dispose the returned buffer after writing the tiles.
    /// </summary>
    public static DecodedImageBuffer DecodeFullRaw(
        string path,
        CancellationToken cancellationToken = default)
    {
        Validate(path);
        cancellationToken.ThrowIfCancellationRequested();

        IntPtr pixels = IntPtr.Zero;
        try
        {
            using var stream = OpenRead(path);
            using var codec = SKCodec.Create(stream)
                ?? throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "SkiaSharp could not read the image.",
                    path,
                    Path.GetExtension(path));

            var orientation = GetOrientation(codec);
            var (sourceWidth, sourceHeight) = orientation.OrientedSize(
                codec.Info.Width,
                codec.Info.Height);
            var pixelCount = (long)codec.Info.Width * codec.Info.Height;
            if (pixelCount <= 0 || pixelCount > int.MaxValue / 4)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The decoded image would be too large.",
                    path,
                    Path.GetExtension(path));
            }

            var info = new SKImageInfo(
                codec.Info.Width,
                codec.Info.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgb());

            unsafe
            {
                pixels = (IntPtr)NativeMemory.Alloc(checked((nuint)(pixelCount * 4)));
            }
            var result = codec.GetPixels(info, pixels);
            cancellationToken.ThrowIfCancellationRequested();

            if (result != SKCodecResult.Success)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    $"The image could not be decoded ({result}).",
                    path,
                    Path.GetExtension(path));
            }

            var buffer = new DecodedImageBuffer(
                pixels,
                codec.Info.Width,
                codec.Info.Height,
                sourceWidth,
                sourceHeight,
                orientation,
                FormatMayHaveAlpha(codec.EncodedFormat));
            pixels = IntPtr.Zero;
            return buffer;
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
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
        finally
        {
            if (pixels != IntPtr.Zero)
            {
                unsafe
                {
                    NativeMemory.Free((void*)pixels);
                }
            }
        }
    }

    public static ImageMetadata ReadMetadata(string path)
    {
        Validate(path);

        try
        {
            var file = new FileInfo(path);
            using var stream = OpenRead(path);
            using var codec = SKCodec.Create(stream)
                ?? throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "SkiaSharp could not read the image.",
                    path,
                    Path.GetExtension(path));

            var header = ImageHeaderReader.Read(path);
            var orientation = GetOrientation(codec);
            if (orientation == ImageOrientation.TopLeft && header.Exif.Orientation is >= 1 and <= 8)
            {
                orientation = (ImageOrientation)header.Exif.Orientation.Value;
            }

            var (displayWidth, displayHeight) = orientation.OrientedSize(codec.Info.Width, codec.Info.Height);

            return new ImageMetadata
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = file.Length,
                LastWriteTime = file.LastWriteTimeUtc,
                RawWidth = codec.Info.Width,
                RawHeight = codec.Info.Height,
                DisplayWidth = displayWidth,
                DisplayHeight = displayHeight,
                Orientation = orientation,
                FormatName = DescribeFormat(codec.EncodedFormat, path),
                BitDepth = header.BitDepth ?? 8,
                IccDescription = IccProfile.ReadDescription(header.IccProfile),
                Exif = header.Exif
            };
        }
        catch (ImageLoadException)
        {
            throw;
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

    private static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new ImageLoadException(
                ImageLoadFailure.NotFound,
                "The file does not exist.",
                path,
                Path.GetExtension(path));
        }

        if (!SupportedFormats.IsSupported(path))
        {
            throw new ImageLoadException(
                ImageLoadFailure.Unsupported,
                "The format is not supported.",
                path,
                Path.GetExtension(path));
        }
    }

    private static FileStream OpenRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

    private static byte[] Downscale(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var sourcePixels = MemoryMarshal.Cast<byte, uint>(source.AsSpan());
        var target = GC.AllocateUninitializedArray<byte>(targetWidth * targetHeight * 4);
        var targetPixels = MemoryMarshal.Cast<byte, uint>(target.AsSpan());

        var xRatio = (double)sourceWidth / targetWidth;
        var yRatio = (double)sourceHeight / targetHeight;
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, (int)((y + 0.5) * yRatio));
            var row = sourceY * sourceWidth;
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, (int)((x + 0.5) * xRatio));
                targetPixels[y * targetWidth + x] = sourcePixels[row + sourceX];
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

    /// <summary>
    /// JPEG decodes at 1/2, 1/4 and 1/8 natively. When the nearest step lands below the
    /// window size the result would look soft, so step up once and let the renderer
    /// downscale instead. The decoded image still fits comfortably in memory.
    /// </summary>
    private static SKSizeI BumpToUsefulScale(
        SKCodec codec,
        SKSizeI scaled,
        ImageOrientation orientation,
        double targetScale,
        int orientedWidth,
        int orientedHeight)
    {
        var displayWidth = Math.Max(1, (int)Math.Round(orientedWidth * targetScale));
        var displayHeight = Math.Max(1, (int)Math.Round(orientedHeight * targetScale));
        var scale = targetScale;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (scaledWidth, scaledHeight) = orientation.OrientedSize(scaled.Width, scaled.Height);
            if (scaledWidth >= displayWidth * 0.92 && scaledHeight >= displayHeight * 0.92)
            {
                break;
            }

            var nextScale = Math.Min(1.0, Math.Max(scale * 1.75, scale + 0.05));
            var next = NormalizeScaled(codec.GetScaledDimensions((float)nextScale), codec.Info);
            if (next.Width <= scaled.Width && next.Height <= scaled.Height)
            {
                break;
            }

            scaled = next;
            scale = nextScale;
        }

        return scaled;
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
