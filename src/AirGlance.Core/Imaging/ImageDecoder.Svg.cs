using AirGlance.Core.Files;
using BitMiracle.LibTiff.Classic;
using System.Runtime.InteropServices;
using SkiaSharp;
using Svg.Skia;
using TiffOrientation = BitMiracle.LibTiff.Classic.Orientation;

namespace AirGlance.Core.Imaging;

public static partial class ImageDecoder
{
    private static DecodedBitmap DecodeSvg(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // One parse: the picture provides the drawable rect, so the size no longer
            // needs a second pass over the document with SvgGeometry.
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

            var sourceWidth = Math.Max(1, (int)Math.Round(cull.Width));
            var sourceHeight = Math.Max(1, (int)Math.Round(cull.Height));
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

            // Draw straight into the managed buffer instead of rendering into an SKBitmap
            // and copying its bytes out afterwards.
            var pixels = GC.AllocateUninitializedArray<byte>(checked(targetWidth * targetHeight * 4));
            var info = new SKImageInfo(
                targetWidth,
                targetHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgb());

            unsafe
            {
                fixed (byte* destination = pixels)
                {
                    using var bitmap = new SKBitmap();
                    if (!bitmap.InstallPixels(info, (IntPtr)destination, targetWidth * 4))
                    {
                        throw new ImageLoadException(
                            ImageLoadFailure.Unknown,
                            "The SVG render target could not be created.",
                            path,
                            ".svg");
                    }

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
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

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
}
