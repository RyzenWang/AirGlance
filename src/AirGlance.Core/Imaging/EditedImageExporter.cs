using NetVips;

namespace AirGlance.Core.Imaging;

public enum EditResizeMode
{
    Original,
    LongEdge,
    FitWithin
}

/// <summary>Options for the edit-mode "Export copy" action.</summary>
public sealed record EditExportOptions(
    ExportFormat Format,
    int Quality,
    EditResizeMode ResizeMode,
    int ResizeValue,
    bool KeepMetadata)
{
    public int NormalizedQuality => Math.Clamp(Quality, 40, 100);

    /// <summary>
    /// Target pixel size after crop, without ever upscaling. The UI can still show the
    /// original crop size when a larger value is typed.
    /// </summary>
    public (int Width, int Height) TargetSize(int width, int height)
    {
        var max = Math.Clamp(ResizeValue, 1, 1_000_000);
        if (ResizeMode == EditResizeMode.Original || max <= 0)
        {
            return (width, height);
        }

        var scale = ResizeMode == EditResizeMode.LongEdge
            ? (double)max / Math.Max(width, height)
            : Math.Min((double)max / width, (double)max / height);

        if (scale >= 1)
        {
            return (width, height);
        }

        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }
}

/// <summary>
/// Writes a transformed, cropped and resized copy through libvips. libvips is used
/// here because it can retain the source's EXIF/ICC/XMP metadata, which Skia's
/// raw-pixel encoder cannot. A managed fallback lives in <see cref="ImageExporter"/>.
/// </summary>
public static class EditedImageExporter
{
    /// <summary>
    /// Exports directly from the source file, applying mirror/rotation in the same
    /// order as <see cref="ImageTransform.Apply"/> and then cropping the result.
    /// The destination must not be the source path and must not exist.
    /// </summary>
    public static void ExportNative(
        string sourcePath,
        EditTransform transform,
        CropRegion crop,
        EditExportOptions options,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The export path must differ from the original.");
        }

        if (File.Exists(destinationPath))
        {
            throw new IOException("The export destination already exists.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Image? working = null;
        Image? mirrored = null;
        Image? rotated = null;
        Image? cropped = null;
        Image? resized = null;
        Image? flattened = null;

        try
        {
            using var source = Image.NewFromFile(sourcePath, access: Enums.Access.Random);
            using var oriented = source.Autorot();
            working = oriented;

            mirrored = transform.MirrorHorizontal ? working.FlipHor() : null;
            var afterMirror = mirrored ?? working;

            rotated = (transform.QuarterTurns & 3) switch
            {
                1 => afterMirror.Rot90(),
                2 => afterMirror.Rot180(),
                3 => afterMirror.Rot270(),
                _ => null
            };
            var afterRotation = rotated ?? afterMirror;

            var (x, y, width, height) = crop.Clamp().ToPixels(afterRotation.Width, afterRotation.Height);
            cropped = x != 0 || y != 0 || width != afterRotation.Width || height != afterRotation.Height
                ? afterRotation.Crop(x, y, width, height)
                : null;
            var afterCrop = cropped ?? afterRotation;

            var (targetWidth, targetHeight) = options.TargetSize(afterCrop.Width, afterCrop.Height);
            var scale = (double)targetWidth / afterCrop.Width;
            resized = scale < 0.999
                ? afterCrop.Resize(scale, kernel: Enums.Kernel.Lanczos3)
                : null;
            var output = resized ?? afterCrop;

            if (options.Format == ExportFormat.Jpeg && output.HasAlpha())
            {
                flattened = output.Flatten(new double[] { 255, 255, 255 });
                output = flattened;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Save(output, options, destinationPath);
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
        finally
        {
            flattened?.Dispose();
            resized?.Dispose();
            cropped?.Dispose();
            rotated?.Dispose();
            mirrored?.Dispose();
        }
    }

    private static void Save(Image image, EditExportOptions options, string path)
    {
        var keep = options.KeepMetadata ? Enums.ForeignKeep.All : Enums.ForeignKeep.None;
        switch (options.Format)
        {
            case ExportFormat.Png:
                image.Pngsave(path, compression: 9, keep: keep);
                break;
            case ExportFormat.WebP:
                image.Webpsave(path, q: options.NormalizedQuality, keep: keep);
                break;
            default:
                image.Jpegsave(path, q: options.NormalizedQuality, keep: keep);
                break;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
