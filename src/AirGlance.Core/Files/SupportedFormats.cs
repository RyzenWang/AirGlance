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

    /// <summary>
    /// Extensions AirGlance recognises but cannot decode. They are worth naming to the
    /// user: a folder of iPhone photos or camera RAW files otherwise looks empty with no
    /// explanation of where the files went.
    /// </summary>
    private static readonly string[] UnsupportedHeicExtensions = [".heic", ".heif", ".hif"];

    private static readonly string[] UnsupportedRawExtensions =
    [
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2"
    ];

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

    /// <summary>True for a format AirGlance knows it cannot decode, such as HEIC or RAW.</summary>
    public static bool IsKnownUnsupported(string? path) =>
        DescribeUnsupported(path) is not null;

    public static bool IsKnownUnsupportedExtension(string? extension) =>
        DescribeUnsupportedExtension(extension) is not null;

    /// <summary>
    /// The family name of a recognised but undecodable format, so the UI can say which
    /// decoder is missing: "HEIC", "RAW", or null when the format is simply unknown.
    /// </summary>
    public static string? DescribeUnsupported(string? path) =>
        DescribeUnsupportedExtension(Path.GetExtension(path));

    public static string? DescribeUnsupportedExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        if (Matches(UnsupportedHeicExtensions, extension))
        {
            return "HEIC";
        }

        return Matches(UnsupportedRawExtensions, extension) ? "RAW" : null;
    }

    private static bool Matches(string[] extensions, string candidate)
    {
        foreach (var extension in extensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
