namespace AirGlance.Core.Files;

/// <summary>
/// The file types AirGlance can display. Formats SkiaSharp cannot decode at all
/// (HEIC, AVIF, RAW, TIFF) are deliberately absent; they are a known limitation,
/// not a pending feature.
/// </summary>
public static class SupportedFormats
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".jpg",
        ".jpeg",
        ".jfif",
        ".png",
        ".webp",
        ".bmp",
        ".gif",
        ".ico"
    ];

    /// <summary>Pattern used by the open-file dialog, for example <c>*.jpg</c>.</summary>
    public static string FileDialogPattern =>
        string.Join(";", Extensions.Select(extension => "*" + extension));

    public static bool IsSupported(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsSupportedExtension(Path.GetExtension(path));

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
            ".bmp" => "BMP",
            ".gif" => "GIF",
            ".ico" => "ICO",
            _ => extension is { Length: > 1 } ? extension[1..].ToUpperInvariant() : "Unknown"
        };
    }
}
