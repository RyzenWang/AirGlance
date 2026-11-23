namespace AirGlance.Core.Imaging;

/// <summary>Inputs that do not come from the pixel buffer.</summary>
public sealed record ImageAnalysisContext(
    long FileBytes,
    JpegInfo? Jpeg = null,
    int FrameCount = 1,
    double DurationSeconds = 0,
    int LoopCount = 1);

public sealed record PaletteColor(RgbColor Color, double Share);

/// <summary>
/// Everything the analysis drawer shows for one decoded image. The result never
/// keeps a reference to the pixel buffer, so it can be cached across navigation.
/// </summary>
public sealed class ImageAnalysisResult
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Pixels with a non-zero alpha; the denominator for every share.</summary>
    public required long PixelCount { get; init; }

    public required int[] Luma { get; init; }

    public required int[] Red { get; init; }

    public required int[] Green { get; init; }

    public required int[] Blue { get; init; }

    public required int[] Hue { get; init; }

    public required int[] Saturation { get; init; }

    public required int[] Alpha { get; init; }

    public required double Mean { get; init; }

    public required int Median { get; init; }

    public required double StdDev { get; init; }

    public required long ShadowClipCount { get; init; }

    public required long HighlightClipCount { get; init; }

    public required long HardBlackCount { get; init; }

    public required long HardWhiteCount { get; init; }

    public required long[] ZoneCounts { get; init; }

    public required double Entropy { get; init; }

    public required int UniqueColorCount { get; init; }

    public required bool UniqueColorCapped { get; init; }

    public required bool HasTransparency { get; init; }

    public required long TransparentCount { get; init; }

    public required long SemiTransparentCount { get; init; }

    public required RgbColor AverageColor { get; init; }

    /// <summary>Mean HSL saturation, 0 to 1.</summary>
    public required double AverageSaturation { get; init; }

    /// <summary>Mean red minus mean blue, -255 to 255.</summary>
    public required double Warmth { get; init; }

    public required IReadOnlyList<PaletteColor> Palette { get; init; }

    /// <summary>0 to 100; relative, not an absolute quality judgement.</summary>
    public required double Sharpness { get; init; }

    public required double NoiseSigma { get; init; }

    public required long FileBytes { get; init; }

    public required int FrameCount { get; init; }

    public required double DurationSeconds { get; init; }

    public required int LoopCount { get; init; }

    public JpegInfo? Jpeg { get; init; }

    public double BytesPerPixel =>
        PixelCount <= 0 ? 0 : FileBytes / (double)(Width * (long)Height);
}
