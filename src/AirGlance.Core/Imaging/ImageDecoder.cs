using AirGlance.Core.Files;
using BitMiracle.LibTiff.Classic;
using System.Runtime.InteropServices;
using SkiaSharp;
using Svg.Skia;
using TiffOrientation = BitMiracle.LibTiff.Classic.Orientation;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Decodes images off the UI thread: EXIF orientation is applied, the embedded ICC
/// profile is converted to sRGB, and downscaled decoding is requested whenever the
/// caller only needs a screen-sized image.
/// </summary>
public static partial class ImageDecoder
{
    /// <summary>
    /// Pixel count above which a full-resolution TIFF raster is refused. The raster
    /// path holds an <c>int</c> buffer and a <c>byte</c> buffer at once, so 32 MP is
    /// already 256 MB plus 128 MB; these files are previewed by sampling instead and
    /// reach 100% through the region reader.
    /// </summary>
    private const long FullRasterPixelBudget = 32_000_000;

    /// <summary>
    /// Pixel count above which a codec that cannot scale down is not decoded at all.
    /// A 128 MP preview would already need a 512 MB buffer.
    /// </summary>
    private const long PreviewPixelBudget = 128_000_000;

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

        if (SupportedFormats.IsTiff(path))
        {
            if (IsLargeTiff(path))
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Unsupported,
                    "Full-resolution decoding is not available for this large TIFF.",
                    path,
                    Path.GetExtension(path));
            }

            return DecodeTiffRaw(path, cancellationToken);
        }

        if (SupportedFormats.IsSvg(path))
        {
            return DecodeSvgRaw(path, cancellationToken);
        }

        IntPtr pixels = IntPtr.Zero;
        try
        {
            if (SupportedFormats.IsAvif(path))
            {
                return LibAvifDecoder.DecodeRaw(path);
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
        if (SupportedFormats.IsTiff(path))
        {
            return ReadTiffMetadata(path);
        }

        if (SupportedFormats.IsSvg(path))
        {
            return ReadSvgMetadata(path);
        }

        try
        {
            if (SupportedFormats.IsAvif(path))
            {
                return LibAvifDecoder.ReadMetadata(path);
            }

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
                FrameCount = Math.Max(1, codec.FrameCount),
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

    /// <summary>
    /// Decodes every frame of an animated GIF. This is intentionally GIF-only:
    /// SkiaSharp exposes frame metadata for WebP too, but the current viewer and
    /// documentation only promise GIF animation. Returns null for still images.
    /// </summary>
    public static DecodedAnimation? DecodeAnimation(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default)
    {
        Validate(path);
        cancellationToken.ThrowIfCancellationRequested();

        // This used to gate on ".gif" because GIF was the only animated format when it was
        // written. SKCodec exposes WebP frames through the same API, and a still image
        // reports a frame count of one below, so the extension check is gone.
        try
        {
            using var stream = OpenRead(path);
            using var codec = SKCodec.Create(stream)
                ?? throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "SkiaSharp could not read the image.",
                    path,
                    ".gif");

            var frameCount = codec.FrameCount;
            if (frameCount <= 1)
            {
                return null;
            }

            var sourceWidth = codec.Info.Width;
            var sourceHeight = codec.Info.Height;
            var targetScale = maxWidth <= 0 || maxHeight <= 0
                ? 1.0
                : Math.Min(1.0, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));
            targetScale = Math.Max(targetScale, 1.0 / 64.0);
            var scaled = NormalizeScaled(codec.GetScaledDimensions((float)targetScale), codec.Info);
            var targetWidth = scaled.Width;
            var targetHeight = scaled.Height;

            // A GIF can contain hundreds of frames. Keep the whole animation under a
            // fixed pixel budget rather than letting one file consume the process.
            const long MaximumAnimationPixels = 24_000_000;
            var totalPixels = (long)targetWidth * targetHeight * frameCount;
            if (totalPixels > MaximumAnimationPixels)
            {
                var factor = Math.Sqrt(MaximumAnimationPixels / (double)totalPixels);
                targetWidth = Math.Max(1, (int)Math.Round(targetWidth * factor));
                targetHeight = Math.Max(1, (int)Math.Round(targetHeight * factor));
            }

            var info = new SKImageInfo(
                targetWidth,
                targetHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgb());
            var frameInfos = codec.FrameInfo;
            var canvas = new byte[checked(targetWidth * targetHeight * 4)];
            var frames = new List<DecodedBitmap>(Math.Min(frameCount, 1024));
            var delays = new List<int>(Math.Min(frameCount, 1024));
            byte[]? snapshotBeforePrevious = null;
            SKCodecFrameInfo? previousInfo = null;
            SKRectI previousRect = SKRectI.Empty;

            for (var index = 0; index < frameCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frameInfo = frameInfos[index];

                if (previousInfo is { } previous)
                {
                    switch (previous.DisposalMethod)
                    {
                        case SKCodecAnimationDisposalMethod.RestoreBackgroundColor:
                            ClearRect(canvas, targetWidth, previousRect);
                            break;
                        case SKCodecAnimationDisposalMethod.RestorePrevious:
                            if (snapshotBeforePrevious is not null)
                            {
                                Buffer.BlockCopy(snapshotBeforePrevious, 0, canvas, 0, canvas.Length);
                            }

                            break;
                    }
                }

                snapshotBeforePrevious = frameInfo.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious
                    ? (byte[])canvas.Clone()
                    : null;

                var raw = GC.AllocateUninitializedArray<byte>(info.Width * info.Height * 4);
                SKCodecResult result;
                unsafe
                {
                    fixed (byte* destination = raw)
                    {
                        result = codec.GetPixels(info, (IntPtr)destination, new SKCodecOptions(index));
                    }
                }

                if (result != SKCodecResult.Success)
                {
                    throw new ImageLoadException(
                        ImageLoadFailure.Corrupt,
                        $"The GIF frame could not be decoded ({result}).",
                        path,
                        ".gif");
                }

                var targetRect = ScaleRect(frameInfo.FrameRect, sourceWidth, sourceHeight, targetWidth, targetHeight);
                CopyFrame(canvas, raw, targetWidth, targetHeight, targetRect, frameInfo.Blend);

                frames.Add(new DecodedBitmap(
                    targetWidth,
                    targetHeight,
                    (byte[])canvas.Clone(),
                    sourceWidth,
                    sourceHeight,
                    targetWidth / (double)sourceWidth)
                {
                    HasAlpha = true
                });
                delays.Add(Math.Clamp(frameInfo.Duration <= 0 ? 100 : frameInfo.Duration, 20, 10_000));

                previousInfo = frameInfo;
                previousRect = targetRect;
            }

            return new DecodedAnimation(frames, delays, codec.RepetitionCount);
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
                ".gif",
                exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.NotFound,
                "The file does not exist.",
                path,
                ".gif",
                exception);
        }
        catch (IOException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Unknown,
                "The file could not be read.",
                path,
                ".gif",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The GIF animation could not be decoded.",
                path,
                ".gif",
                exception);
        }
    }

    /// <summary>
    /// Samples tiles or strips into the requested size and then applies the stored
    /// orientation to the small result. Bounded memory regardless of the source size.
    /// </summary>
    private static DecodedBitmap DecodeTiffPreview(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        var preview = TiffPreviewDecoder.DecodePreview(path, maxWidth, maxHeight, cancellationToken);

        // DecodePreview always reports a proxy because it was written for files past the
        // raster budget. Below that budget a full-resolution decode is still possible, so
        // the bitmap is a plain preview and the viewer keeps its normal 100% path.
        var pixelCount = (long)preview.SourceWidth * preview.SourceHeight;
        if (preview.IsProxy && pixelCount <= FullRasterPixelBudget)
        {
            preview = new DecodedBitmap(
                preview.Width,
                preview.Height,
                preview.Pixels,
                preview.SourceWidth,
                preview.SourceHeight,
                preview.Scale);
        }

        var orientation = TiffPreviewDecoder.ReadOrientation(path);
        if (orientation == ImageOrientation.TopLeft)
        {
            return preview;
        }

        var (sourceWidth, sourceHeight) = orientation.OrientedSize(preview.SourceWidth, preview.SourceHeight);
        return ImageTransform.ApplyOrientation(preview, orientation, sourceWidth, sourceHeight);
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

    /// <summary>
    /// The orientation from the file's own EXIF block, for codecs that do not report an
    /// encoded origin. Failures fall back to "no rotation" so a metadata problem can
    /// never stop an image from opening.
    /// </summary>
    private static ImageOrientation ManagedOrientation(string path)
    {
        try
        {
            var header = ImageHeaderReader.Read(path);
            return header.Exif.Orientation is >= 1 and <= 8
                ? (ImageOrientation)header.Exif.Orientation.Value
                : ImageOrientation.TopLeft;
        }
        catch
        {
            return ImageOrientation.TopLeft;
        }
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

}
