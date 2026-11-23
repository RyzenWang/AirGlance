namespace AirGlance.Core.Imaging;

/// <summary>
/// JPEG coding details read from the header. Quality is inferred from the
/// quantization tables and is therefore an estimate; the remaining fields are exact.
/// </summary>
public sealed class JpegInfo
{
    public int? EstimatedQuality { get; init; }

    /// <summary>"4:2:0", "4:2:2", "4:4:4", or empty when the factors are unusual.</summary>
    public string Subsampling { get; init; } = string.Empty;

    public bool Progressive { get; init; }

    public bool Baseline { get; init; }

    public int? RestartInterval { get; init; }
}
