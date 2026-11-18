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

        if (SupportedFormats.IsTiff(path))
        {
            return DecodeTiffRaw(path, cancellationToken);
        }

        if (SupportedFormats.IsSvg(path))
        {
            return DecodeSvgRaw(path, cancellationToken);
        }

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

        if (!string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

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

    private static DecodedBitmap DecodeTiff(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            using var image = OpenTiff(path);
            var info = ReadTiffInfo(image, path);
            var (orientedWidth, orientedHeight) = info.Orientation.OrientedSize(info.Width, info.Height);
            var bitmap = ReadTiffRgba(image, info, path);
            cancellationToken.ThrowIfCancellationRequested();
            var rgba = new byte[checked(bitmap.Length * sizeof(int))];
            Buffer.BlockCopy(bitmap, 0, rgba, 0, rgba.Length);
            var raw = new DecodedBitmap(
                info.Width,
                info.Height,
                rgba,
                info.Width,
                info.Height,
                1.0)
            {
                HasAlpha = true
            };
            var oriented = ImageTransform.ApplyOrientation(
                raw,
                info.Orientation,
                info.Width,
                info.Height);

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                return oriented;
            }

            var scale = Math.Min(
                1.0,
                Math.Min((double)maxWidth / orientedWidth, (double)maxHeight / orientedHeight));
            if (scale >= 0.99)
            {
                return oriented;
            }

            var targetWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));
            var resized = Downscale(
                oriented.Pixels,
                oriented.Width,
                oriented.Height,
                targetWidth,
                targetHeight);
            return new DecodedBitmap(
                targetWidth,
                targetHeight,
                resized,
                orientedWidth,
                orientedHeight,
                targetWidth / (double)orientedWidth)
            {
                HasAlpha = true
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
        catch (UnauthorizedAccessException exception)
        {
            throw TiffFailure(ImageLoadFailure.AccessDenied, "The file could not be read because access was denied.", path, exception);
        }
        catch (FileNotFoundException exception)
        {
            throw TiffFailure(ImageLoadFailure.NotFound, "The file does not exist.", path, exception);
        }
        catch (IOException exception)
        {
            throw TiffFailure(ImageLoadFailure.Unknown, "The file could not be read.", path, exception);
        }
        catch (Exception exception)
        {
            throw TiffFailure(ImageLoadFailure.Corrupt, "The TIFF could not be decoded.", path, exception);
        }
    }

    private static DecodedImageBuffer DecodeTiffRaw(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var image = OpenTiff(path);
            var info = ReadTiffInfo(image, path);
            var (orientedWidth, orientedHeight) = info.Orientation.OrientedSize(info.Width, info.Height);
            var bitmap = ReadTiffRgba(image, info, path);
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = checked(bitmap.Length * sizeof(int));
            IntPtr pixels = IntPtr.Zero;
            unsafe
            {
                pixels = (IntPtr)NativeMemory.Alloc((nuint)bytes);
                fixed (int* source = bitmap)
                {
                    Buffer.MemoryCopy(source, (void*)pixels, bytes, bytes);
                }
            }

            var buffer = new DecodedImageBuffer(
                pixels,
                info.Width,
                info.Height,
                orientedWidth,
                orientedHeight,
                info.Orientation,
                true);
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
            throw TiffFailure(ImageLoadFailure.AccessDenied, "The file could not be read because access was denied.", path, exception);
        }
        catch (FileNotFoundException exception)
        {
            throw TiffFailure(ImageLoadFailure.NotFound, "The file does not exist.", path, exception);
        }
        catch (IOException exception)
        {
            throw TiffFailure(ImageLoadFailure.Unknown, "The file could not be read.", path, exception);
        }
        catch (Exception exception)
        {
            throw TiffFailure(ImageLoadFailure.Corrupt, "The TIFF could not be decoded.", path, exception);
        }
    }

    private static ImageMetadata ReadTiffMetadata(string path)
    {
        try
        {
            var file = new FileInfo(path);
            using var image = OpenTiff(path);
            var info = ReadTiffInfo(image, path);
            var (displayWidth, displayHeight) = info.Orientation.OrientedSize(info.Width, info.Height);
            return new ImageMetadata
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = file.Length,
                LastWriteTime = file.LastWriteTimeUtc,
                RawWidth = info.Width,
                RawHeight = info.Height,
                DisplayWidth = displayWidth,
                DisplayHeight = displayHeight,
                Orientation = info.Orientation,
                FormatName = "TIFF",
                FrameCount = 1,
                BitDepth = info.BitsPerSample ?? 8,
                IccDescription = IccProfile.ReadDescription(info.IccProfile)
            };
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw TiffFailure(ImageLoadFailure.AccessDenied, "The file could not be read because access was denied.", path, exception);
        }
        catch (FileNotFoundException exception)
        {
            throw TiffFailure(ImageLoadFailure.NotFound, "The file does not exist.", path, exception);
        }
        catch (IOException exception)
        {
            throw TiffFailure(ImageLoadFailure.Unknown, "The file could not be read.", path, exception);
        }
        catch (Exception exception)
        {
            throw TiffFailure(ImageLoadFailure.Corrupt, "The TIFF could not be read.", path, exception);
        }
    }

    private static Tiff OpenTiff(string path)
    {
        var image = Tiff.Open(path, "r");
        if (image is null)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The TIFF could not be opened.",
                path,
                Path.GetExtension(path));
        }

        return image;
    }

    private static TiffInfo ReadTiffInfo(Tiff image, string path)
    {
        var width = TiffInt(image, TiffTag.IMAGEWIDTH);
        var height = TiffInt(image, TiffTag.IMAGELENGTH);
        if (width is not > 0 || height is not > 0)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The TIFF has no usable image dimensions.",
                path,
                Path.GetExtension(path));
        }

        var pixelCount = (long)width.Value * height.Value;
        if (pixelCount > int.MaxValue / 4)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The decoded TIFF would be too large.",
                path,
                Path.GetExtension(path));
        }

        var orientationValue = TiffInt(image, TiffTag.ORIENTATION) ?? 1;
        var orientation = orientationValue is >= 1 and <= 8
            ? (ImageOrientation)orientationValue
            : ImageOrientation.TopLeft;
        return new TiffInfo(
            width.Value,
            height.Value,
            orientation,
            TiffInt(image, TiffTag.BITSPERSAMPLE),
            TiffBytes(image, TiffTag.ICCPROFILE));
    }

    private static int[] ReadTiffRgba(Tiff image, TiffInfo info, string path)
    {
        var raster = new int[checked(info.Width * info.Height)];
        // LibTiff's oriented reader expects the orientation that is stored in the
        // file; passing the tag back gives us the unrotated raster. Our own
        // ImageTransform then applies that orientation exactly once.
        if (!image.ReadRGBAImageOriented(
                info.Width,
                info.Height,
                raster,
                ToTiffOrientation(info.Orientation)))
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The TIFF pixels could not be decoded.",
                path,
                Path.GetExtension(path));
        }

        // LibTiff fills the int raster as RGBA in little-endian byte order. Avalonia
        // wants BGRA, so exchange the red and blue channels once here.
        for (var index = 0; index < raster.Length; index++)
        {
            var value = (uint)raster[index];
            var red = value & 0xFF;
            var green = value & 0xFF00;
            var blue = (value >> 16) & 0xFF;
            var alpha = value & 0xFF000000;
            raster[index] = (int)(alpha | (red << 16) | green | blue);
        }

        return raster;
    }

    private static TiffOrientation ToTiffOrientation(ImageOrientation orientation) =>
        orientation switch
        {
            ImageOrientation.TopRight => TiffOrientation.TOPRIGHT,
            ImageOrientation.BottomRight => TiffOrientation.BOTRIGHT,
            ImageOrientation.BottomLeft => TiffOrientation.BOTLEFT,
            ImageOrientation.LeftTop => TiffOrientation.LEFTTOP,
            ImageOrientation.RightTop => TiffOrientation.RIGHTTOP,
            ImageOrientation.RightBottom => TiffOrientation.RIGHTBOT,
            ImageOrientation.LeftBottom => TiffOrientation.LEFTBOT,
            _ => TiffOrientation.TOPLEFT
        };

    private static int? TiffInt(Tiff image, TiffTag tag)
    {
        try
        {
            var values = image.GetField(tag);
            return values is { Length: > 0 } ? values[0].ToInt() : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TiffBytes(Tiff image, TiffTag tag)
    {
        try
        {
            var values = image.GetField(tag);
            return values is { Length: > 0 } ? values[0].GetBytes() : null;
        }
        catch
        {
            return null;
        }
    }

    private static ImageLoadException TiffFailure(
        ImageLoadFailure failure,
        string message,
        string path,
        Exception exception) =>
        new(failure, message, path, Path.GetExtension(path), exception);

    private sealed record TiffInfo(
        int Width,
        int Height,
        ImageOrientation Orientation,
        int? BitsPerSample,
        byte[]? IccProfile);

    private static DecodedBitmap DecodeSvg(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (sourceWidth, sourceHeight) = SvgGeometry.Read(path);
            var targetScale = maxWidth <= 0 || maxHeight <= 0
                ? 1.0
                : Math.Min(1.0, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));
            targetScale = Math.Max(targetScale, 1.0 / 64.0);

            var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * targetScale));
            var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * targetScale));
            var pixelCount = (long)targetWidth * targetHeight;
            if (pixelCount <= 0 || pixelCount > int.MaxValue / 4)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The decoded SVG would be too large.",
                    path,
                    ".svg");
            }

            using var stream = OpenRead(path);
            using var svg = new SKSvg();
            var picture = svg.Load(stream);
            cancellationToken.ThrowIfCancellationRequested();

            var cull = picture?.CullRect ?? SKRect.Empty;
            if (picture is null || cull.Width <= 0 || cull.Height <= 0)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The SVG has no drawable area.",
                    path,
                    ".svg");
            }

            using var bitmap = new SKBitmap(new SKImageInfo(
                targetWidth,
                targetHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgb()));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                var drawScale = Math.Min(targetWidth / cull.Width, targetHeight / cull.Height);
                canvas.Translate(
                    (float)((targetWidth - cull.Width * drawScale) / 2),
                    (float)((targetHeight - cull.Height * drawScale) / 2));
                canvas.Scale((float)drawScale);
                canvas.Translate(-cull.Left, -cull.Top);
                canvas.DrawPicture(picture);
                canvas.Flush();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var pixels = bitmap.Bytes;
            if (pixels.Length < targetWidth * targetHeight * 4)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The SVG renderer returned an incomplete image.",
                    path,
                    ".svg");
            }

            return new DecodedBitmap(
                targetWidth,
                targetHeight,
                pixels,
                sourceWidth,
                sourceHeight,
                targetWidth / (double)sourceWidth)
            {
                HasAlpha = true
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
        catch (UnauthorizedAccessException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.AccessDenied,
                "The file could not be read because access was denied.",
                path,
                ".svg",
                exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.NotFound,
                "The file does not exist.",
                path,
                ".svg",
                exception);
        }
        catch (IOException exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Unknown,
                "The file could not be read.",
                path,
                ".svg",
                exception);
        }
        catch (Exception exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The SVG could not be rendered.",
                path,
                ".svg",
                exception);
        }
    }

    private static DecodedImageBuffer DecodeSvgRaw(string path, CancellationToken cancellationToken)
    {
        var decoded = DecodeSvg(path, 0, 0, cancellationToken);
        IntPtr pixels = IntPtr.Zero;
        try
        {
            unsafe
            {
                pixels = (IntPtr)NativeMemory.Alloc(checked((nuint)decoded.Pixels.Length));
                fixed (byte* source = decoded.Pixels)
                {
                    Buffer.MemoryCopy(source, (void*)pixels, decoded.Pixels.Length, decoded.Pixels.Length);
                }
            }

            var buffer = new DecodedImageBuffer(
                pixels,
                decoded.Width,
                decoded.Height,
                decoded.SourceWidth,
                decoded.SourceHeight,
                ImageOrientation.TopLeft,
                true);
            pixels = IntPtr.Zero;
            return buffer;
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

    private static ImageMetadata ReadSvgMetadata(string path)
    {
        var file = new FileInfo(path);
        var (width, height) = SvgGeometry.Read(path);
        return new ImageMetadata
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            FileSizeBytes = file.Length,
            LastWriteTime = file.LastWriteTimeUtc,
            RawWidth = width,
            RawHeight = height,
            DisplayWidth = width,
            DisplayHeight = height,
            FormatName = "SVG",
            FrameCount = 1,
            BitDepth = 8
        };
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
