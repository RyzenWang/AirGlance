using System.Globalization;
using AirGlance.Core.Imaging;
using Avalonia.Media;

namespace AirGlance.App.ViewModels;

public sealed class PaletteSwatch
{
    public required IBrush Brush { get; init; }

    public required string Hex { get; init; }

    public required string Share { get; init; }
}

public sealed class ZoneBarItem
{
    public required double Height { get; init; }

    public required IBrush Brush { get; init; }

    public required string Tooltip { get; init; }
}

/// <summary>
/// A formatted, immutable snapshot of one analysis result. The view binds to a single
/// <see cref="AnalysisDisplay"/> so a result swap is one property change, and a language
/// change can rebuild it without re-analysing the pixels.
/// </summary>
public sealed class AnalysisDisplay
{
    private static readonly Color[] ZoneColors =
    [
        Color.Parse("#4A545C"), Color.Parse("#59636B"), Color.Parse("#68727A"),
        Color.Parse("#788289"), Color.Parse("#899298"), Color.Parse("#9BA3A7"),
        Color.Parse("#AEB5B7"), Color.Parse("#C2C8C7"), Color.Parse("#D6D9D4"),
        Color.Parse("#E8E5DC"), Color.Parse("#F1E8D8")
    ];

    public required IReadOnlyList<int> Luma { get; init; }

    public required IReadOnlyList<int> Red { get; init; }

    public required IReadOnlyList<int> Green { get; init; }

    public required IReadOnlyList<int> Blue { get; init; }

    public required IReadOnlyList<int> Hue { get; init; }

    public required IReadOnlyList<int> SaturationBins { get; init; }

    public required string MeanText { get; init; }

    public required string MedianText { get; init; }

    public required string StdDevText { get; init; }

    public required string ShadowClipText { get; init; }

    public required string HighlightClipText { get; init; }

    public required bool ShadowClipHit { get; init; }

    public required bool HighlightClipHit { get; init; }

    public required string HardClipText { get; init; }

    public required IReadOnlyList<ZoneBarItem> Zones { get; init; }

    public required IReadOnlyList<PaletteSwatch> Palette { get; init; }

    public required IBrush AverageBrush { get; init; }

    public required string AverageHex { get; init; }

    public required string AverageSaturationText { get; init; }

    public required string WarmthText { get; init; }

    public required string SharpnessText { get; init; }

    public required double SharpnessFraction { get; init; }

    public required string SharpnessGrade { get; init; }

    public required string NoiseText { get; init; }

    public required string NoiseGrade { get; init; }

    public required bool NoiseLow { get; init; }

    public required bool NoiseMedium { get; init; }

    public required bool NoiseHigh { get; init; }

    public required bool HasJpeg { get; init; }

    public required string JpegQualityText { get; init; }

    public required string JpegDetailText { get; init; }

    public required string BytesPerPixelText { get; init; }

    public required string ExposureClipsText { get; init; }

    public required string ToneGrade { get; init; }

    public required double ToneMarker { get; init; }

    public required string P99Text { get; init; }

    public required IReadOnlyList<InfoSection> DataSections { get; init; }

    public static AnalysisDisplay Empty(AnalysisLabels labels) => new()
    {
        Luma = [],
        Red = [],
        Green = [],
        Blue = [],
        Hue = [],
        SaturationBins = [],
        MeanText = "—",
        MedianText = "—",
        StdDevText = "—",
        ShadowClipText = "—",
        HighlightClipText = "—",
        ShadowClipHit = false,
        HighlightClipHit = false,
        HardClipText = string.Empty,
        Zones = [],
        Palette = [],
        AverageBrush = new SolidColorBrush(Color.Parse("#00000000")),
        AverageHex = "—",
        AverageSaturationText = $"{labels.Saturation} —",
        WarmthText = labels.Neutral,
        SharpnessText = "—",
        SharpnessFraction = 0,
        SharpnessGrade = "—",
        NoiseText = "—",
        NoiseGrade = "—",
        NoiseLow = false,
        NoiseMedium = false,
        NoiseHigh = false,
        HasJpeg = false,
        JpegQualityText = "—",
        JpegDetailText = "—",
        BytesPerPixelText = "—",
        ExposureClipsText = "—",
        ToneGrade = "—",
        ToneMarker = 0,
        P99Text = "—",
        DataSections = []
    };

    public static AnalysisDisplay Build(
        ImageAnalysisResult result,
        ImageMetadata metadata,
        AnalysisLabels labels,
        CultureInfo culture,
        string resolutionNote)
    {
        var totalPixels = Math.Max(1, result.Width * (long)result.Height);
        var pixelCount = Math.Max(0, result.PixelCount);

        var maxZone = 0L;
        foreach (var count in result.ZoneCounts)
        {
            maxZone = Math.Max(maxZone, count);
        }

        var zones = new List<ZoneBarItem>(result.ZoneCounts.Length);
        for (var i = 0; i < result.ZoneCounts.Length; i++)
        {
            var share = pixelCount == 0 ? 0 : result.ZoneCounts[i] / (double)pixelCount;
            var height = maxZone == 0 ? 3 : 3 + result.ZoneCounts[i] / (double)maxZone * 30;
            zones.Add(new ZoneBarItem
            {
                Height = Math.Clamp(height, 3, 33),
                Brush = new SolidColorBrush(ZoneColors[Math.Min(i, ZoneColors.Length - 1)]),
                Tooltip = $"{i} · {share * 100:0.0}%"
            });
        }

        var palette = new List<PaletteSwatch>(result.Palette.Count);
        foreach (var entry in result.Palette)
        {
            palette.Add(new PaletteSwatch
            {
                Brush = new SolidColorBrush(Color.FromRgb(entry.Color.R, entry.Color.G, entry.Color.B)),
                Hex = entry.Color.ToHex(),
                Share = (entry.Share * 100).ToString("0.0", culture) + "%"
            });
        }

        var sharpnessGrade = result.Sharpness >= 65
            ? labels.Sharp
            : result.Sharpness >= 35
                ? labels.Soft
                : labels.Blurry;
        var noiseGrade = result.NoiseSigma < 2
            ? labels.Low
            : result.NoiseSigma <= 5
                ? labels.Medium
                : labels.High;
        var toneGrade = result.Median < 64
            ? labels.ToneShadow
            : result.Median <= 192
                ? labels.ToneMid
                : labels.ToneHighlight;
        var warmth = result.Warmth;
        var warmthText = Math.Abs(warmth) <= 6
            ? labels.Neutral
            : warmth > 0
                ? labels.Warm
                : labels.Cool;

        var p99 = Percentile(result.Luma, pixelCount, 0.99);
        var semiTransparent = result.SemiTransparentCount;
        var transparent = result.TransparentCount;
        var opaque = Math.Max(0, totalPixels - transparent - semiTransparent);
        var alphaText = result.HasTransparency
            ? string.Format(
                culture,
                labels.AlphaCoverageFormat,
                Percent(opaque, totalPixels),
                Percent(transparent + semiTransparent, totalPixels))
            : labels.Opaque;

        var frames = result.FrameCount <= 1
            ? $"1 · {labels.StillImage}"
            : result.FrameCount.ToString(culture);
        var duration = result.FrameCount <= 1
            ? "—"
            : result.DurationSeconds.ToString("0.0", culture) + " s";
        var loop = result.FrameCount <= 1
            ? "—"
            : result.LoopCount < 0
                ? labels.Forever
                : result.LoopCount.ToString(culture);

        var dimensions = string.Format(
            CultureInfo.InvariantCulture,
            "{0} × {1}",
            metadata.DisplayWidth,
            metadata.DisplayHeight);
        var megapixels = string.Format(culture, labels.MegapixelsFormat, metadata.Megapixels);
        var aspect = AspectRatio(metadata.DisplayWidth, metadata.DisplayHeight);
        var bitDepth = string.Format(culture, labels.BitDepthFormat, metadata.BitDepth);
        var sourcePixels = Math.Max(1, metadata.DisplayWidth * (long)metadata.DisplayHeight);
        var bytesPerPixel = result.FileBytes / (double)sourcePixels;

        var sections = new List<InfoSection>
        {
            new(
                labels.DataImage,
                [
                    new InfoRow(labels.Dimensions, $"{dimensions} · {megapixels} · {aspect}", isMono: true),
                    new InfoRow(labels.Format, $"{metadata.FormatName} · {bitDepth} · {metadata.ColorSpaceDisplay}", isMono: true),
                    new InfoRow(labels.AnalysisResolution, resolutionNote, isMono: true)
                ]),
            new(
                labels.DataFile,
                [
                    new InfoRow(
                        labels.Size,
                        $"{FormatBytes(result.FileBytes)} · {bytesPerPixel.ToString("0.00", culture)} B/px",
                        isMono: true)
                ]),
            new(
                labels.DataColor,
                [
                    new InfoRow(labels.AlphaRow, alphaText, isMono: true),
                    new InfoRow(
                        labels.UniqueColors,
                        result.UniqueColorCapped
                            ? string.Format(culture, labels.AtLeastFormat, "65,536")
                            : result.UniqueColorCount.ToString("N0", culture),
                        isMono: true),
                    new InfoRow(labels.Entropy, result.Entropy.ToString("0.00", culture) + " bit", isMono: true)
                ]),
            new(
                labels.DataAnimation,
                [
                    new InfoRow(labels.Frames, frames, isMono: true),
                    new InfoRow(labels.Duration, duration, isMono: true),
                    new InfoRow(labels.Loop, loop, isMono: true)
                ])
        };

        var jpeg = result.Jpeg;
        var jpegDetail = jpeg is null
            ? "—"
            : string.IsNullOrEmpty(jpeg.Subsampling)
                ? jpeg.Progressive ? labels.Progressive : labels.Baseline
                : $"{jpeg.Subsampling} · {(jpeg.Progressive ? labels.Progressive : labels.Baseline)}";

        return new AnalysisDisplay
        {
            Luma = result.Luma,
            Red = result.Red,
            Green = result.Green,
            Blue = result.Blue,
            Hue = result.Hue,
            SaturationBins = result.Saturation,
            MeanText = result.Mean.ToString("0.0", culture),
            MedianText = result.Median.ToString(culture),
            StdDevText = result.StdDev.ToString("0.0", culture),
            ShadowClipText = Percent(result.ShadowClipCount, pixelCount),
            HighlightClipText = Percent(result.HighlightClipCount, pixelCount),
            ShadowClipHit = pixelCount > 0 && result.ShadowClipCount / (double)pixelCount >= 0.0005,
            HighlightClipHit = pixelCount > 0 && result.HighlightClipCount / (double)pixelCount >= 0.0005,
            HardClipText = string.Format(
                culture,
                labels.HardClipFormat,
                Percent(result.HardBlackCount, pixelCount),
                Percent(result.HardWhiteCount, pixelCount)),
            Zones = zones,
            Palette = palette,
            AverageBrush = new SolidColorBrush(Color.FromRgb(
                result.AverageColor.R,
                result.AverageColor.G,
                result.AverageColor.B)),
            AverageHex = result.AverageColor.ToHex(),
            AverageSaturationText = $"{labels.Saturation} {(result.AverageSaturation * 100).ToString("0.0", culture)}%",
            WarmthText = warmthText,
            SharpnessText = Math.Round(result.Sharpness).ToString(culture),
            SharpnessFraction = result.Sharpness / 100,
            SharpnessGrade = sharpnessGrade,
            NoiseText = "σ " + result.NoiseSigma.ToString("0.0", culture),
            NoiseGrade = noiseGrade,
            NoiseLow = result.NoiseSigma < 2,
            NoiseMedium = result.NoiseSigma >= 2 && result.NoiseSigma <= 5,
            NoiseHigh = result.NoiseSigma > 5,
            HasJpeg = jpeg is not null,
            JpegQualityText = jpeg?.EstimatedQuality is int quality
                ? string.Format(culture, labels.EstimatedQualityFormat, quality)
                : "—",
            JpegDetailText = jpegDetail,
            BytesPerPixelText = string.Format(
                culture,
                labels.BytesPerPixelFormat,
                bytesPerPixel.ToString("0.00", culture)),
            ExposureClipsText = string.Format(
                culture,
                labels.ExposureClipsFormat,
                Percent(result.ShadowClipCount, pixelCount),
                Percent(result.HighlightClipCount, pixelCount)),
            ToneGrade = toneGrade,
            ToneMarker = result.Median / 255.0,
            P99Text = string.Format(culture, labels.P99Format, p99),
            DataSections = sections
        };
    }

    private static string Percent(long count, long total) =>
        (total <= 0 ? 0 : count * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static int Percentile(int[] histogram, long pixelCount, double percentile)
    {
        if (pixelCount <= 0)
        {
            return 0;
        }

        var target = Math.Max(1, (long)Math.Ceiling(pixelCount * percentile));
        long cumulative = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target)
            {
                return i;
            }
        }

        return histogram.Length - 1;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        return bytes >= 1024
            ? (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"
            : bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private static string AspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return "—";
        }

        var ratio = width / (double)height;
        var common = new (int Width, int Height)[]
        {
            (1, 1), (5, 4), (4, 3), (3, 2), (16, 10), (16, 9), (2, 1), (21, 9), (3, 1)
        };

        foreach (var candidate in common)
        {
            if (Math.Abs(ratio - candidate.Width / (double)candidate.Height) < 0.01)
            {
                return $"{candidate.Width}:{candidate.Height}";
            }
        }

        var divisor = GreatestCommonDivisor(width, height);
        if (divisor > 0 && width / divisor <= 50 && height / divisor <= 50)
        {
            return $"{width / divisor}:{height / divisor}";
        }

        return ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
