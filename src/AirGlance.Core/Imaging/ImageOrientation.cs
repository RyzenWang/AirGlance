namespace AirGlance.Core.Imaging;

/// <summary>The eight EXIF orientations, in the order the specification numbers them.</summary>
public enum ImageOrientation
{
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 3,
    BottomLeft = 4,
    LeftTop = 5,
    RightTop = 6,
    RightBottom = 7,
    LeftBottom = 8
}

public static class ImageOrientationExtensions
{
    public static bool SwapsAxes(this ImageOrientation orientation) =>
        orientation is ImageOrientation.LeftTop
            or ImageOrientation.RightTop
            or ImageOrientation.RightBottom
            or ImageOrientation.LeftBottom;

    public static ImageOrientation Normalize(this ImageOrientation orientation) =>
        Enum.IsDefined(orientation) ? orientation : ImageOrientation.TopLeft;

    public static (int Width, int Height) OrientedSize(
        this ImageOrientation orientation,
        int width,
        int height) =>
        orientation.SwapsAxes() ? (height, width) : (width, height);
}
