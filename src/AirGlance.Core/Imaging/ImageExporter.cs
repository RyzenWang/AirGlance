using SkiaSharp;

namespace AirGlance.Core.Imaging;

public enum ExportFormat
{
    Jpeg,
    Png,
    WebP
}

public static class ExportFormats
{
    public static string DisplayName(this ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => "JPEG",
        ExportFormat.Png => "PNG",
        ExportFormat.WebP => "WebP",
        _ => "JPEG"
    };

    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Png => ".png",
        ExportFormat.WebP => ".webp",
        _ => ".jpg"
    };

    /// <summary>MIME-ish filter text for file pickers, for example "JPEG image".</summary>
    public static string Description(this ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => "JPEG image",
        ExportFormat.Png => "PNG image",
        ExportFormat.WebP => "WebP image",
        _ => "Image"
    };

    public static ExportFormat FromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ExportFormat.Png,
            ".webp" => ExportFormat.WebP,
            _ => ExportFormat.Jpeg
        };

    public static IReadOnlyList<ExportFormat> All { get; } =
        [ExportFormat.Jpeg, ExportFormat.Png, ExportFormat.WebP];

    internal static SKEncodedImageFormat ToSkia(this ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        ExportFormat.Png => SKEncodedImageFormat.Png,
        ExportFormat.WebP => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Jpeg
    };
}

/// <summary>
/// Encodes the image the viewer currently shows and writes it beside the original.
/// Every write goes to a new file: the original is never opened for writing.
/// </summary>
public static class ImageExporter
{
    public static byte[] Encode(DecodedBitmap image, ExportFormat format, int quality = 90)
    {
        ArgumentNullException.ThrowIfNull(image);
        quality = Math.Clamp(quality, 1, 100);

        var info = new SKImageInfo(
            image.Width,
            image.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        unsafe
        {
            fixed (byte* pixels = image.Pixels)
            {
                using var bitmap = new SKBitmap();
                if (!bitmap.InstallPixels(info, (IntPtr)pixels, image.Stride))
                {
                    throw new InvalidOperationException("The image pixels could not be wrapped for encoding.");
                }

                if (format == ExportFormat.Jpeg)
                {
                    using var flattened = new SKBitmap(info);
                    using (var canvas = new SKCanvas(flattened))
                    {
                        canvas.Clear(SKColors.White);
                        canvas.DrawBitmap(
                            bitmap,
                            0,
                            0,
                            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                            null);
                        canvas.Flush();
                    }

                    using var flattenedImage = SKImage.FromBitmap(flattened);
                    return EncodeImage(flattenedImage, format, quality);
                }

                using var image2 = SKImage.FromBitmap(bitmap);
                return EncodeImage(image2, format, quality);
            }
        }
    }

    /// <summary>Default "save a copy" name, for example <c>photo-copy.jpg</c>.</summary>
    public static string GetDefaultCopyPath(string sourcePath, ExportFormat format)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))
            ?? throw new ArgumentException("The source path has no directory.", nameof(sourcePath));
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = format.Extension();
        return NextAvailablePath(Path.Combine(directory, $"{name}-copy{extension}"));
    }

    /// <summary>Adds -2, -3, ... until the path does not exist.</summary>
    public static string NextAvailablePath(string requestedPath)
    {
        var fullPath = Path.GetFullPath(requestedPath);
        if (!File.Exists(fullPath))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The path has no directory.", nameof(requestedPath));
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Writes the encoded bytes to a new file, retrying with a suffix if another process
    /// creates the same name first. Returns the path actually written.
    /// </summary>
    public static string WriteNewFile(string requestedPath, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var path = NextAvailablePath(requestedPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
                return path;
            }
            catch (IOException) when (attempt < 8)
            {
                path = NextAvailablePath(path);
            }
        }
    }

    public static long EstimateMemoryFootprint(DecodedBitmap image, ExportFormat format) =>
        format switch
        {
            ExportFormat.Png => (long)image.Width * image.Height * 4,
            _ => (long)image.Width * image.Height * 3 / 8
        };

    private static byte[] EncodeImage(SKImage image, ExportFormat format, int quality)
    {
        using var data = image.Encode(format.ToSkia(), quality);
        if (data is null || data.Size == 0)
        {
            throw new InvalidOperationException($"The {format.DisplayName()} encoder returned no data.");
        }

        return data.ToArray();
    }
}
