using AirGlance.Core.Files;
using BitMiracle.LibTiff.Classic;
using System.Runtime.InteropServices;
using SkiaSharp;
using Svg.Skia;
using TiffOrientation = BitMiracle.LibTiff.Classic.Orientation;

namespace AirGlance.Core.Imaging;

public static partial class ImageDecoder
{
    private static DecodedBitmap DecodeTiff(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        // A preview never needs the full raster: sampling tiles or strips straight into
        // the target is what keeps a 100 MP TIFF from building two full-size buffers
        // (an int[] and a byte[]) before shrinking them.
        if (maxWidth > 0 && maxHeight > 0)
        {
            return DecodeTiffPreview(path, maxWidth, maxHeight, cancellationToken);
        }

        try
        {
            using var image = OpenTiff(path);
            var info = ReadTiffInfo(image, path);
            EnsureFullRasterFits(info, path);
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
                // SourceWidth/SourceHeight describe the pixels at 100% zoom, which is the
                // orientation-applied size; the raw source pair made a rotated TIFF report
                // its stored width and a scale below one.
                orientedWidth,
                orientedHeight);

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

    private static void EnsureFullRasterFits(TiffInfo info, string path)
    {
        if ((long)info.Width * info.Height > FullRasterPixelBudget)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "This TIFF is too large to decode at full resolution; it can still be shown as a preview.",
                path,
                Path.GetExtension(path));
        }
    }

    private static ImageMetadata ReadLargeTiffMetadata(string path) =>
        // The preview decoder describes the stored raster; the info panel shows what the
        // viewer displays, which is that raster after the TIFF orientation tag.
        TiffPreviewDecoder.ReadMetadata(path).WithOrientation(TiffPreviewDecoder.ReadOrientation(path));

    private static DecodedImageBuffer DecodeTiffRaw(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var image = OpenTiff(path);
            var info = ReadTiffInfo(image, path);
            EnsureFullRasterFits(info, path);
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
            if (IsLargeTiff(path))
            {
                return ReadLargeTiffMetadata(path);
            }

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
                IccDescription = IccProfile.ReadDescription(info.IccProfile),

                // LibTiff hands back the stored samples. Nothing converts an embedded
                // TIFF profile to sRGB, so the info panel must not claim it did.
                IccAppliedToPixels = false
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

    private static bool IsLargeTiff(string path)
    {
        try
        {
            using var image = OpenTiff(path);
            var width = TiffInt(image, TiffTag.IMAGEWIDTH);
            var height = TiffInt(image, TiffTag.IMAGELENGTH);
            return width is > 0 && height is > 0 &&
                   (long)width.Value * height.Value > FullRasterPixelBudget;
        }
        catch
        {
            return true;
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
                ImageLoadFailure.TooLarge,
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
}
