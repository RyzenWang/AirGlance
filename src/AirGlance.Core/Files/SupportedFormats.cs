using AirGlance.Core.Imaging;

namespace AirGlance.Core.Files;

/// <summary>
/// The file types AirGlance can display. HEIC and camera RAW remain outside the
/// supported set; TIFF uses LibTiff and AVIF uses the bundled libavif decoder.
/// </summary>
public static class SupportedFormats
{
    private static readonly string[] AllExtensions =
    [
        ".jpg",
        ".jpeg",
        ".jfif",
        ".png",
        ".webp",
        ".avif",
        ".bmp",
        ".gif",
        ".ico",
        ".svg",
        ".tif",
        ".tiff"
    ];

    /// <summary>
    /// AVIF is only advertised when the per-RID native decoder is present. A build
    /// without it must not list a format it cannot open.
    /// </summary>
    public static bool SupportsAvif => AvifNative.IsAvailable;

    public static readonly IReadOnlyList<string> Extensions = AllExtensions
        .Where(extension => extension != ".avif" || SupportsAvif)
        .ToArray();

    /// <summary>Pattern used by the open-file dialog, for example <c>*.jpg</c>.</summary>
    public static string FileDialogPattern =>
        string.Join(";", Extensions.Select(extension => "*" + extension));

    public static bool IsSupported(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsSupportedExtension(Path.GetExtension(path));

    public static bool IsSvg(string? path) =>
        string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);

    public static bool IsTiff(string? path) =>
        string.Equals(Path.GetExtension(path), ".tif", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".tiff", StringComparison.OrdinalIgnoreCase);

    public static bool IsAvif(string? path) =>
        string.Equals(Path.GetExtension(path), ".avif", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        foreach (var supported in Extensions)
        {
            if (string.Equals(supported, extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Human-readable name for an extension, used when the decoder cannot be asked.</summary>
    public static string Describe(string? path)
    {
        var extension = Path.GetExtension(path)?.ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".jfif" => "JPEG",
            ".png" => "PNG",
            ".webp" => "WebP",
            ".avif" => "AVIF",
            ".bmp" => "BMP",
            ".gif" => "GIF",
            ".ico" => "ICO",
            ".svg" => "SVG",
            ".tif" or ".tiff" => "TIFF",
            _ => extension is { Length: > 1 } ? extension[1..].ToUpperInvariant() : "Unknown"
        };
    }
}
