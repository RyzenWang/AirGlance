using NetVips;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Reads individual BGRA tiles from a large TIFF through libvips random access.
/// The image handle stays open for the lifetime of the region viewer; no full
/// raster is materialized.
/// </summary>
public sealed class TiffRegionReader : IDisposable
{
    private readonly object _gate = new();
    private Image? _image;
    private bool _disposed;

    private TiffRegionReader(Image image)
    {
        _image = image;
        PixelWidth = image.Width;
        PixelHeight = image.Height;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public static TiffRegionReader Open(string path)
    {
        var image = Image.NewFromFile(path, access: Enums.Access.Random);
        return new TiffRegionReader(image);
    }

    public byte[] ReadTile(int x, int y, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var image = _image ?? throw new ObjectDisposedException(nameof(TiffRegionReader));
            width = Math.Min(width, PixelWidth - x);
            height = Math.Min(height, PixelHeight - y);
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            using var tile = image.Crop(x, y, width, height);
            var source = tile.WriteToMemory<byte>();
            var bands = tile.Bands;
            var result = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
            for (var index = 0; index < width * height; index++)
            {
                byte red;
                byte green;
                byte blue;
                byte alpha = 255;
                if (bands == 1)
                {
                    red = green = blue = source[index];
                }
                else if (bands == 2)
                {
                    red = green = blue = source[index * 2];
                    alpha = source[index * 2 + 1];
                }
                else if (bands == 3)
                {
                    var offset = index * 3;
                    red = source[offset];
                    green = source[offset + 1];
                    blue = source[offset + 2];
                }
                else
                {
                    var offset = index * 4;
                    red = source[offset];
                    green = source[offset + 1];
                    blue = source[offset + 2];
                    alpha = source[offset + 3];
                }

                if (alpha != 255)
                {
                    red = (byte)(red * alpha / 255);
                    green = (byte)(green * alpha / 255);
                    blue = (byte)(blue * alpha / 255);
                }

                var target = index * 4;
                result[target] = blue;
                result[target + 1] = green;
                result[target + 2] = red;
                result[target + 3] = alpha;
            }

            return result;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _image?.Dispose();
            _image = null;
        }
    }
}
