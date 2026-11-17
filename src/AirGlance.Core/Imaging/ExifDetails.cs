namespace AirGlance.Core.Imaging;

/// <summary>The EXIF fields the information panel can show. Missing values stay null.</summary>
public sealed class ExifDetails
{
    public static readonly ExifDetails Empty = new();

    public string? CameraMake { get; init; }

    public string? CameraModel { get; init; }

    public string? LensModel { get; init; }

    public double? ExposureSeconds { get; init; }

    public double? FNumber { get; init; }

    public int? Iso { get; init; }

    public double? FocalLengthMillimeters { get; init; }

    public double? ExposureBias { get; init; }

    public DateTime? TakenAt { get; init; }

    public string? TakenAtRaw { get; init; }

    public int? Orientation { get; init; }

    /// <summary>"Canon" + "Canon EOS R5" should not become "Canon Canon EOS R5".</summary>
    public string? Camera
    {
        get
        {
            var make = CameraMake?.Trim();
            var model = CameraModel?.Trim();

            if (string.IsNullOrEmpty(make))
            {
                return string.IsNullOrEmpty(model) ? null : model;
            }

            if (string.IsNullOrEmpty(model))
            {
                return make;
            }

            return model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";
        }
    }

    public bool IsEmpty =>
        Camera is null
        && LensModel is null
        && ExposureSeconds is null
        && FNumber is null
        && Iso is null
        && FocalLengthMillimeters is null
        && TakenAtRaw is null;
}
