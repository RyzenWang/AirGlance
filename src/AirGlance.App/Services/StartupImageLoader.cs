using AirGlance.Core.Files;
using AirGlance.Core.Imaging;

namespace AirGlance.App.Services;

/// <summary>
/// Decodes the first image while the window is still being built. By the time the
/// viewer asks for a screen-sized preview the pixels are usually already waiting, which
/// moves the expensive part of "open an image" off the first-frame path.
/// </summary>
public sealed class PreloadedImage
{
    public PreloadedImage(string path, ImageMetadata metadata, DecodedBitmap bitmap, double decodeMilliseconds)
    {
        Path = path;
        Metadata = metadata;
        Bitmap = bitmap;
        DecodeMilliseconds = decodeMilliseconds;
    }

    public string Path { get; }

    public ImageMetadata Metadata { get; }

    public DecodedBitmap Bitmap { get; }

    public double DecodeMilliseconds { get; }
}

public static class StartupImageLoader
{
    public const int TargetWidth = 1600;
    public const int TargetHeight = 1000;

    public static PreloadedImage? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var imagePath = path;

            if (Directory.Exists(imagePath))
            {
                var files = FolderScanner.EnumerateImages(imagePath);
                if (files.Count == 0)
                {
                    return null;
                }

                imagePath = files[0];
            }

            if (!File.Exists(imagePath) || !SupportedFormats.IsSupported(imagePath))
            {
                return null;
            }

            var metadata = ImageDecoder.ReadMetadata(imagePath);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var bitmap = ImageDecoder.Decode(imagePath, TargetWidth, TargetHeight);
            stopwatch.Stop();

            return new PreloadedImage(
                imagePath,
                metadata,
                bitmap,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch
        {
            // A preload is an optimisation; the real load reports the error.
            return null;
        }
    }
}
