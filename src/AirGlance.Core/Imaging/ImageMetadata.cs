namespace AirGlance.Core.Imaging;

/// <summary>Everything the information panel needs, read without decoding the pixels.</summary>
public sealed class ImageMetadata
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required long FileSizeBytes { get; init; }

    public required DateTimeOffset LastWriteTime { get; init; }

    /// <summary>Stored width, before EXIF orientation.</summary>
    public required int RawWidth { get; init; }

    public required int RawHeight { get; init; }

    /// <summary>Display width, after EXIF orientation.</summary>
    public required int DisplayWidth { get; init; }

    public required int DisplayHeight { get; init; }

    public ImageOrientation Orientation { get; init; } = ImageOrientation.TopLeft;

    public required string FormatName { get; init; }

    /// <summary>Number of frames in an animated GIF; 1 for still images.</summary>
    public int FrameCount { get; init; } = 1;

    public bool IsAnimated => FrameCount > 1;

    public int BitDepth { get; init; } = 8;

    /// <summary>Description of the embedded ICC profile, or null when the file has none.</summary>
    public string? IccDescription { get; init; }

    public ExifDetails Exif { get; init; } = ExifDetails.Empty;

    public bool HasIccProfile => !string.IsNullOrWhiteSpace(IccDescription);

    /// <summary>What the viewer shows, for example "Display P3 → sRGB".</summary>
    public string ColorSpaceDisplay =>
        !HasIccProfile || IccDescription!.Contains("sRGB", StringComparison.OrdinalIgnoreCase)
            ? "sRGB"
            : $"{IccDescription} → sRGB";

    public double Megapixels => DisplayWidth * (double)DisplayHeight / 1_000_000d;
}
