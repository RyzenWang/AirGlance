using System.Runtime.InteropServices;

namespace AirGlance.Core.Imaging;

/// <summary>
/// AVIF still-image decoding through the bundled libavif + dav1d shim. The first
/// frame is decoded as premultiplied BGRA; irot/imir, clap and EXIF orientation are
/// applied here so the rest of the pipeline sees an ordinary oriented bitmap.
/// </summary>
internal static unsafe class LibAvifDecoder
{
    private const uint TransformClap = 1u << 1;
    private const uint TransformIrot = 1u << 2;
    private const uint TransformImir = 1u << 3;

    private const int MaxIccBytes = 4 * 1024 * 1024;
    private const int MaxExifBytes = 1 * 1024 * 1024;
    private const long MaxFileBytes = 512L * 1024 * 1024;

    public static ImageMetadata ReadMetadata(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var data = ReadPayload(path);
            var (info, icc, exif) = ReadInfo(data);
            var (rawWidth, rawHeight) = CropSize(info);
            var orientation = ResolveOrientation(info, exif);
            var (displayWidth, displayHeight) = orientation.OrientedSize(rawWidth, rawHeight);

            return new ImageMetadata
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = file.Length,
                LastWriteTime = file.LastWriteTimeUtc,
                RawWidth = rawWidth,
                RawHeight = rawHeight,
                DisplayWidth = displayWidth,
                DisplayHeight = displayHeight,
                Orientation = orientation,
                FormatName = "AVIF",
                FrameCount = 1,
                BitDepth = (int)info.Depth,
                IccDescription = IccProfile.ReadDescription(icc),
                IccAppliedToPixels = false,
                Exif = exif.Length == 0 ? ExifDetails.Empty : ExifReader.Read(exif)
            };
        }
        catch (Exception exception) when (IsNativeFailure(exception))
        {
            throw Translate(exception, path);
        }
    }

    public static DecodedBitmap Decode(string path)
    {
        try
        {
            var data = ReadPayload(path);
            var (info, _, exif) = ReadInfo(data);
            var (width, height) = CropSize(info);

            // The native decoder always writes the whole coded frame and exposes the
            // clean aperture separately, so the buffer must be sized for the frame and
            // then cropped; sizing it from the crop makes the shim reject every file
            // that carries a real clap.
            var frameByteCount = CheckedByteCount((int)info.Width, (int)info.Height, path);

            var pixels = GC.AllocateUninitializedArray<byte>(frameByteCount);
            (uint Width, uint Height, int HasAlpha) decoded;
            fixed (byte* destination = pixels)
            {
                decoded = AvifNative.Decode(data, destination, pixels.Length);
            }

            if (decoded.Width != info.Width || decoded.Height != info.Height)
            {
                throw Failure(ImageLoadFailure.Corrupt, "The AVIF decoder returned unexpected dimensions.", path);
            }

            if (HasClap(info))
            {
                pixels = CropManaged(pixels, (int)info.Width, (int)info.Height, info);
            }

            var orientation = ResolveOrientation(info, exif);
            var (displayWidth, displayHeight) = orientation.OrientedSize(width, height);
            var bitmap = new DecodedBitmap(width, height, pixels, displayWidth, displayHeight, 1.0)
            {
                HasAlpha = decoded.HasAlpha != 0
            };
            return ImageTransform.ApplyOrientation(bitmap, orientation, displayWidth, displayHeight);
        }
        catch (Exception exception) when (IsNativeFailure(exception))
        {
            throw Translate(exception, path);
        }
    }

    public static DecodedImageBuffer DecodeRaw(string path)
    {
        try
        {
            var data = ReadPayload(path);
            var (info, _, exif) = ReadInfo(data);
            var (width, height) = CropSize(info);

            // See Decode: the buffer covers the coded frame, then CropUnmanaged trims it.
            var frameByteCount = CheckedByteCount((int)info.Width, (int)info.Height, path);

            IntPtr pixels = (IntPtr)NativeMemory.Alloc((nuint)frameByteCount);
            try
            {
                var decoded = AvifNative.Decode(data, (byte*)pixels, frameByteCount);
                if (decoded.Width != info.Width || decoded.Height != info.Height)
                {
                    throw Failure(ImageLoadFailure.Corrupt, "The AVIF decoder returned unexpected dimensions.", path);
                }

                if (HasClap(info))
                {
                    pixels = CropUnmanaged(pixels, (int)info.Width, (int)info.Height, info);
                }

                var orientation = ResolveOrientation(info, exif);
                var (displayWidth, displayHeight) = orientation.OrientedSize(width, height);
                var buffer = new DecodedImageBuffer(
                    pixels,
                    width,
                    height,
                    displayWidth,
                    displayHeight,
                    orientation,
                    decoded.HasAlpha != 0);
                pixels = IntPtr.Zero;
                return buffer;
            }
            finally
            {
                if (pixels != IntPtr.Zero)
                {
                    NativeMemory.Free((void*)pixels);
                }
            }
        }
        catch (Exception exception) when (IsNativeFailure(exception))
        {
            throw Translate(exception, path);
        }
    }

    private static (AvifInfo Info, byte[] Icc, byte[] Exif) ReadInfo(ReadOnlySpan<byte> data)
    {
        var info = AvifNative.ReadInfo(data, Span<byte>.Empty, Span<byte>.Empty);
        var iccSize = (int)Math.Min((ulong)info.IccSize, (ulong)MaxIccBytes);
        var exifSize = (int)Math.Min((ulong)info.ExifSize, (ulong)MaxExifBytes);
        var icc = iccSize > 0 ? new byte[iccSize] : [];
        var exif = exifSize > 0 ? new byte[exifSize] : [];

        if (iccSize > 0 || exifSize > 0)
        {
            info = AvifNative.ReadInfo(data, icc, exif);
        }

        return (info, icc, exif);
    }

    private static byte[] ReadPayload(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        var length = stream.Length;
        if (length <= 0 || length > MaxFileBytes)
        {
            throw Failure(ImageLoadFailure.TooLarge, "The AVIF file is empty or too large.", path);
        }

        var data = GC.AllocateUninitializedArray<byte>((int)length);
        stream.ReadExactly(data);
        return data;
    }

    private static int CheckedByteCount(int width, int height, string path)
    {
        var pixelCount = (long)width * height;
        if (pixelCount <= 0 || pixelCount > int.MaxValue / 4)
        {
            throw Failure(ImageLoadFailure.TooLarge, "The decoded AVIF would be too large.", path);
        }

        return (int)pixelCount * 4;
    }

    private static bool HasClap(in AvifInfo info)
    {
        if ((info.TransformFlags & TransformClap) == 0 ||
            info.ClapWidth == 0 ||
            info.ClapHeight == 0)
        {
            return false;
        }

        if (info.ClapX == 0 &&
            info.ClapY == 0 &&
            info.ClapWidth == info.Width &&
            info.ClapHeight == info.Height)
        {
            return false;
        }

        return (ulong)info.ClapX + info.ClapWidth <= info.Width &&
               (ulong)info.ClapY + info.ClapHeight <= info.Height;
    }

    private static (int Width, int Height) CropSize(in AvifInfo info) =>
        HasClap(info)
            ? ((int)info.ClapWidth, (int)info.ClapHeight)
            : ((int)info.Width, (int)info.Height);

    private static byte[] CropManaged(byte[] source, int sourceWidth, int sourceHeight, in AvifInfo info)
    {
        _ = sourceHeight;
        var width = (int)info.ClapWidth;
        var height = (int)info.ClapHeight;
        var destination = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        var rowBytes = width * 4;

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = (((int)info.ClapY + row) * sourceWidth + (int)info.ClapX) * 4;
            Buffer.BlockCopy(source, sourceOffset, destination, row * rowBytes, rowBytes);
        }

        return destination;
    }

    private static IntPtr CropUnmanaged(IntPtr source, int sourceWidth, int sourceHeight, in AvifInfo info)
    {
        _ = sourceHeight;
        var width = (int)info.ClapWidth;
        var height = (int)info.ClapHeight;
        var rowBytes = width * 4;
        var destination = (IntPtr)NativeMemory.Alloc((nuint)checked(width * height * 4));

        for (var row = 0; row < height; row++)
        {
            Buffer.MemoryCopy(
                (byte*)source + (((int)info.ClapY + row) * sourceWidth + (int)info.ClapX) * 4,
                (byte*)destination + row * rowBytes,
                rowBytes,
                rowBytes);
        }

        NativeMemory.Free((void*)source);
        return destination;
    }

    private static ImageOrientation ResolveOrientation(in AvifInfo info, byte[] exif)
    {
        var hasIrot = (info.TransformFlags & TransformIrot) != 0;
        var hasImir = (info.TransformFlags & TransformImir) != 0;
        if (hasIrot || hasImir)
        {
            return MapIrotImir(hasIrot, info.IrotAngle, hasImir, info.ImirAxis);
        }

        if (exif.Length > 0)
        {
            var details = ExifReader.Read(exif);
            if (details.Orientation is >= 1 and <= 8)
            {
                return (ImageOrientation)details.Orientation.Value;
            }
        }

        return ImageOrientation.TopLeft;
    }

    /// <summary>
    /// The same composition libavif documents for Exif orientation to irot/imir:
    /// irot is applied before imir, and all eight combinations map to Exif values.
    /// </summary>
    private static ImageOrientation MapIrotImir(bool hasIrot, uint angle, bool hasImir, uint axis)
    {
        var rotation = hasIrot ? (int)(angle & 3) : 0;
        var mirror = hasImir ? (int)(axis & 1) : -1;

        return (rotation, mirror) switch
        {
            (0, -1) => ImageOrientation.TopLeft,
            (0, 0) => ImageOrientation.BottomLeft,
            (0, 1) => ImageOrientation.TopRight,
            (1, -1) => ImageOrientation.LeftBottom,
            (1, 0) => ImageOrientation.LeftTop,
            (1, 1) => ImageOrientation.RightBottom,
            (2, -1) => ImageOrientation.BottomRight,
            (2, 0) => ImageOrientation.TopRight,
            (2, 1) => ImageOrientation.BottomLeft,
            (3, -1) => ImageOrientation.RightTop,
            (3, 0) => ImageOrientation.RightBottom,
            (3, 1) => ImageOrientation.LeftTop,
            _ => ImageOrientation.TopLeft
        };
    }

    private static bool IsNativeFailure(Exception exception) =>
        exception is AvifNativeException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException;

    private static ImageLoadException Translate(Exception exception, string path) =>
        exception switch
        {
            AvifNativeException native => Failure(ImageLoadFailure.Corrupt, native.Message, path, native),
            DllNotFoundException => Failure(ImageLoadFailure.Unsupported, "The AVIF decoder is not available.", path, exception),
            BadImageFormatException => Failure(ImageLoadFailure.Unsupported, "The AVIF decoder could not be loaded.", path, exception),
            EntryPointNotFoundException => Failure(ImageLoadFailure.Unsupported, "The AVIF decoder is incomplete.", path, exception),
            _ => Failure(ImageLoadFailure.Corrupt, "The AVIF could not be decoded.", path, exception)
        };

    private static ImageLoadException Failure(
        ImageLoadFailure failure,
        string message,
        string path,
        Exception? inner = null) =>
        new(failure, message, path, ".avif", inner);
}
