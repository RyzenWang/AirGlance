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
