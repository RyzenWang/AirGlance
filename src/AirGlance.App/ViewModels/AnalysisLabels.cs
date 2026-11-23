using AirGlance.App.Services;

namespace AirGlance.App.ViewModels;

/// <summary>
/// Static strings for the analysis drawer. Replaced as a whole when the language
/// changes, so every binding under <c>Labels.*</c> refreshes at once.
/// </summary>
public sealed class AnalysisLabels
{
    public AnalysisLabels(LocalizationService localization)
    {
        Title = localization.T("L.AnalysisTitle");
        Histogram = localization.T("L.AnalysisHistogram");
        Color = localization.T("L.AnalysisColor");
        Quality = localization.T("L.AnalysisQuality");
        Data = localization.T("L.AnalysisData");
        Luma = localization.T("L.AnalysisLuma");
        Rgb = "RGB";
        Hue = localization.T("L.AnalysisHue");
        Saturation = localization.T("L.AnalysisSaturation");
        Mean = localization.T("L.AnalysisMean");
        Median = localization.T("L.AnalysisMedian");
        StdDev = localization.T("L.AnalysisStdDev");
        Shadows = localization.T("L.AnalysisShadows");
        Highlights = localization.T("L.AnalysisHighlights");
        ToneZones = localization.T("L.AnalysisToneZones");
        ToneZonesRange = localization.T("L.AnalysisToneZonesRange");
        ZoneDark = localization.T("L.AnalysisZoneDark");
        ZoneMid = localization.T("L.AnalysisZoneMid");
        ZoneBright = localization.T("L.AnalysisZoneBright");
        HardClipFormat = localization.T("L.AnalysisHardClipFormat");
        LowSaturationNote = localization.T("L.AnalysisLowSatNote");
        Palette = localization.T("L.AnalysisPalette");
        ByShare = localization.T("L.AnalysisByShare");
        AverageColor = localization.T("L.AnalysisAverageColor");
        Warm = localization.T("L.AnalysisWarm");
        Neutral = localization.T("L.AnalysisNeutral");
        Cool = localization.T("L.AnalysisCool");
        Eyedropper = localization.T("L.AnalysisEyedropper");
        EyedropperHint = localization.T("L.AnalysisEyedropperHint");
        PreviewCoordinates = localization.T("L.AnalysisPreviewCoordinates");
        PinnedSamples = localization.T("L.AnalysisPinnedSamples");
        PinnedCountFormat = localization.T("L.AnalysisPinnedCount");
        PinHint = localization.T("L.AnalysisPinHint");
        NoSample = localization.T("L.AnalysisNoSample");
        NoSampleActive = localization.T("L.AnalysisNoSampleActive");
        RgbLabel = "RGB";
        HslLabel = "HSL";
        LabLabel = "LAB";
        AlphaLabel = localization.T("L.AnalysisAlphaLabel");
        Sharpness = localization.T("L.AnalysisSharpness");
        Noise = localization.T("L.AnalysisNoise");
        Compression = localization.T("L.AnalysisCompression");
        Exposure = localization.T("L.AnalysisExposure");
        Sharp = localization.T("L.AnalysisSharp");
        Soft = localization.T("L.AnalysisSoft");
        Blurry = localization.T("L.AnalysisBlurry");
        Low = localization.T("L.AnalysisLow");
        Medium = localization.T("L.AnalysisMedium");
        High = localization.T("L.AnalysisHigh");
        SharpMethod = localization.T("L.AnalysisSharpMethod");
        Relative = localization.T("L.AnalysisRelative");
        NoiseMethod = localization.T("L.AnalysisNoiseMethod");
        Estimate = localization.T("L.AnalysisEstimate");
        JpegMethod = localization.T("L.AnalysisJpegMethod");
        Progressive = localization.T("L.AnalysisProgressive");
        Baseline = localization.T("L.AnalysisBaseline");
        BitDepthFormat = localization.T("L.BitDepthFormat");
        MegapixelsFormat = localization.T("L.MegapixelsFormat");
        QualityNote1 = localization.T("L.AnalysisQualityNote1");
        QualityNote2 = localization.T("L.AnalysisQualityNote2");
        QualityNote3 = localization.T("L.AnalysisQualityNote3");
        Methodology = localization.T("L.AnalysisMethodology");
        ExposureClipsFormat = localization.T("L.AnalysisExposureClipsFormat");
        ToneShadow = localization.T("L.AnalysisToneShadow");
        ToneMid = localization.T("L.AnalysisToneMid");
        ToneHighlight = localization.T("L.AnalysisToneHighlight");
        ToneCenter = localization.T("L.AnalysisToneCenter");
        P99Format = localization.T("L.AnalysisP99Format");
        BytesPerPixelFormat = localization.T("L.AnalysisBytesPerPixelFormat");
        EstimatedQualityFormat = localization.T("L.AnalysisEstimatedQualityFormat");
        DataImage = localization.T("L.AnalysisDataImage");
        DataFile = localization.T("L.AnalysisDataFile");
        DataColor = localization.T("L.AnalysisDataColor");
        DataAnimation = localization.T("L.AnalysisDataAnimation");
        Dimensions = localization.T("L.InfoDimensions");
        Format = localization.T("L.Format");
        Size = localization.T("L.AnalysisSize");
        AnalysisResolution = localization.T("L.AnalysisAnalysisResolution");
        AlphaRow = localization.T("L.AnalysisAlphaLabel");
        UniqueColors = localization.T("L.AnalysisUniqueColors");
        Entropy = localization.T("L.AnalysisEntropy");
        Frames = localization.T("L.InfoFrames");
        Duration = localization.T("L.AnalysisDuration");
        Loop = localization.T("L.AnalysisLoop");
        Opaque = localization.T("L.AnalysisOpaque");
        StillImage = localization.T("L.AnalysisStillImage");
        Forever = localization.T("L.AnalysisForever");
        AtLeastFormat = localization.T("L.AnalysisAtLeastFormat");
        AlphaCoverageFormat = localization.T("L.AnalysisAlphaCoverageFormat");
        PreviewNoteFormat = localization.T("L.AnalysisPreviewNote");
        OriginalNoteFormat = localization.T("L.AnalysisOriginalNote");
        Preview = localization.T("L.AnalysisPreview");
        Original = localization.T("L.AnalysisOriginal");
        Analyzing = localization.T("L.AnalysisAnalyzing");
        Close = localization.T("L.AnalysisClose");
        Failed = localization.T("L.AnalysisFailed");
        Unavailable = localization.T("L.AnalysisUnavailable");
        DataNote = localization.T("L.AnalysisDataNote");
    }

    public string Title { get; }
    public string Histogram { get; }
    public string Color { get; }
    public string Quality { get; }
    public string Data { get; }
    public string Luma { get; }
    public string Rgb { get; }
    public string Hue { get; }
    public string Saturation { get; }
    public string Mean { get; }
    public string Median { get; }
    public string StdDev { get; }
    public string Shadows { get; }
    public string Highlights { get; }
    public string ToneZones { get; }
    public string ToneZonesRange { get; }
    public string ZoneDark { get; }
    public string ZoneMid { get; }
    public string ZoneBright { get; }
    public string HardClipFormat { get; }
    public string LowSaturationNote { get; }
    public string Palette { get; }
    public string ByShare { get; }
    public string AverageColor { get; }
    public string Warm { get; }
    public string Neutral { get; }
    public string Cool { get; }
    public string Eyedropper { get; }
    public string EyedropperHint { get; }
    public string PreviewCoordinates { get; }
    public string PinnedSamples { get; }
    public string PinnedCountFormat { get; }
    public string PinHint { get; }
    public string NoSample { get; }
    public string NoSampleActive { get; }
    public string RgbLabel { get; }
    public string HslLabel { get; }
    public string LabLabel { get; }
    public string AlphaLabel { get; }
    public string Sharpness { get; }
    public string Noise { get; }
    public string Compression { get; }
    public string Exposure { get; }
    public string Sharp { get; }
    public string Soft { get; }
    public string Blurry { get; }
    public string Low { get; }
    public string Medium { get; }
    public string High { get; }
    public string SharpMethod { get; }
    public string Relative { get; }
    public string NoiseMethod { get; }
    public string Estimate { get; }
    public string JpegMethod { get; }
    public string Progressive { get; }
    public string Baseline { get; }
    public string BitDepthFormat { get; }
    public string MegapixelsFormat { get; }
    public string QualityNote1 { get; }
    public string QualityNote2 { get; }
    public string QualityNote3 { get; }
    public string Methodology { get; }
    public string ExposureClipsFormat { get; }
    public string ToneShadow { get; }
    public string ToneMid { get; }
    public string ToneHighlight { get; }
    public string ToneCenter { get; }
    public string P99Format { get; }
    public string BytesPerPixelFormat { get; }
    public string EstimatedQualityFormat { get; }
    public string DataImage { get; }
    public string DataFile { get; }
    public string DataColor { get; }
    public string DataAnimation { get; }
    public string Dimensions { get; }
    public string Format { get; }
    public string Size { get; }
    public string AnalysisResolution { get; }
    public string AlphaRow { get; }
    public string UniqueColors { get; }
    public string Entropy { get; }
    public string Frames { get; }
    public string Duration { get; }
    public string Loop { get; }
    public string Opaque { get; }
    public string StillImage { get; }
    public string Forever { get; }
    public string AtLeastFormat { get; }
    public string AlphaCoverageFormat { get; }
    public string PreviewNoteFormat { get; }
    public string OriginalNoteFormat { get; }
    public string Preview { get; }
    public string Original { get; }
    public string Analyzing { get; }
    public string Close { get; }
    public string Failed { get; }
    public string Unavailable { get; }
    public string DataNote { get; }
}
