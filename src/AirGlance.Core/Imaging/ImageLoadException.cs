namespace AirGlance.Core.Imaging;

public enum ImageLoadFailure
{
    NotFound,
    AccessDenied,
    Unsupported,
    Corrupt,
    Unknown
}

/// <summary>A failure the window can explain in the user's language.</summary>
public sealed class ImageLoadException : Exception
{
    public ImageLoadException(ImageLoadFailure failure, string message, string? path = null, string? extension = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        Path = path;
        Extension = extension;
    }

    public ImageLoadFailure Failure { get; }

    public string? Path { get; }

    public string? Extension { get; }
}
