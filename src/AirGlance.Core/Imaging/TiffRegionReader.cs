using BitMiracle.LibTiff.Classic;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Reads individual BGRA tiles from a TIFF through the managed LibTiff.NET codec.
/// Tiled images are decoded one tile at a time and stripped images one strip at a
/// time, so a 40K TIFF never needs a full-size managed raster. The image handle
/// stays open for the lifetime of the region viewer.
/// </summary>
public sealed class TiffRegionReader : IDisposable
{
    // One strip raster larger than this would defeat the point of region reading.
    // A single-strip 40K image needs a streaming decoder, which is a follow-up.
    private const long MaximumStripRasterBytes = 64L * 1024 * 1024;

    // Adjacent 512 px tiles share the same strips. Keeping a few decoded strips
    // turns a stripped image from one strip decode per tile into one per band.
    private const long MaximumStripCacheBytes = 96L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Tiff _image;
    private readonly bool _tiled;
    private readonly int _tileWidth;
    private readonly int _tileHeight;
    private readonly int _rowsPerStrip;
    private readonly Dictionary<int, int[]> _stripCache = [];
    private readonly LinkedList<int> _stripOrder = [];
    private int[] _tileRaster = [];
    private long _stripCacheBytes;
    private bool _disposed;

    private TiffRegionReader(Tiff image, string path, int width, int height)
    {
        _image = image;
        _path = path;
        PixelWidth = width;
        PixelHeight = height;
        _tiled = image.IsTiled();
        if (_tiled)
        {
            _tileWidth = TagInt(image, TiffTag.TILEWIDTH);
            _tileHeight = TagInt(image, TiffTag.TILELENGTH);
            if (_tileWidth <= 0 || _tileHeight <= 0)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The TIFF has no usable tile geometry.",
                    path,
                    Path.GetExtension(path));
            }
        }
        else
        {
            var rows = TagLong(image, TiffTag.ROWSPERSTRIP);
            _rowsPerStrip = rows <= 0 || rows > height ? height : (int)rows;
        }
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public static TiffRegionReader Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Tiff? image = null;
        try
        {
            image = Tiff.Open(path, "r");
            if (image is null)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The TIFF could not be opened.",
                    path,
                    Path.GetExtension(path));
            }

            var width = TagInt(image, TiffTag.IMAGEWIDTH);
            var height = TagInt(image, TiffTag.IMAGELENGTH);
            if (width <= 0 || height <= 0)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.Corrupt,
                    "The TIFF has no usable image dimensions.",
                    path,
                    Path.GetExtension(path));
            }

            // LibTiff's RGBA reader applies the orientation tag to the returned
            // raster. The region path returns the stored raster and lets
            // ImageTransform apply the orientation once, so ask the reader for
            // the stored order. The tag is changed in the in-memory directory
            // only; the file is opened read-only and is not rewritten.
            var orientation = TagInt(image, TiffTag.ORIENTATION);
            if (orientation is > 1 and <= 8)
            {
                image.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
            }

            var reader = new TiffRegionReader(image, path, width, height);
            image = null;
            return reader;
        }
        catch (ImageLoadException)
        {
            image?.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            image?.Dispose();
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The TIFF could not be opened.",
                path,
                Path.GetExtension(path),
                exception);
        }
    }

    public byte[] ReadTile(int x, int y, int width, int height)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            width = Math.Min(width, PixelWidth - x);
            height = Math.Min(height, PixelHeight - y);
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            var byteCount = (long)width * height * 4;
            if (byteCount > int.MaxValue)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.TooLarge,
                    "The requested TIFF region is too large.",
                    _path,
                    Path.GetExtension(_path));
            }

            var result = GC.AllocateUninitializedArray<byte>((int)byteCount);
            if (_tiled)
            {
                ReadTiled(x, y, width, height, result);
            }
            else
            {
                ReadStripped(x, y, width, height, result);
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
            _image.Dispose();
            _stripCache.Clear();
            _stripOrder.Clear();
            _stripCacheBytes = 0;
        }
    }

    private void ReadTiled(int x, int y, int width, int height, byte[] destination)
    {
        var rasterLength = (long)_tileWidth * _tileHeight;
        if (rasterLength > int.MaxValue)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF tile is too large for region reading.",
                _path,
                Path.GetExtension(_path));
        }

        if (_tileRaster.Length < rasterLength)
        {
            _tileRaster = new int[(int)rasterLength];
        }

        var raster = _tileRaster;
        var endX = x + width;
        var endY = y + height;
        var firstTileX = x / _tileWidth * _tileWidth;
        var firstTileY = y / _tileHeight * _tileHeight;

        for (var tileY = firstTileY; tileY < endY; tileY += _tileHeight)
        {
            for (var tileX = firstTileX; tileX < endX; tileX += _tileWidth)
            {
                if (!_image.ReadRGBATile(tileX, tileY, raster))
                {
                    throw new ImageLoadException(
                        ImageLoadFailure.Corrupt,
                        "The TIFF tile could not be decoded.",
                        _path,
                        Path.GetExtension(_path));
                }

                var left = Math.Max(x, tileX);
                var top = Math.Max(y, tileY);
                var right = Math.Min(endX, tileX + _tileWidth);
                var bottom = Math.Min(endY, tileY + _tileHeight);
                for (var pixelY = top; pixelY < bottom; pixelY++)
                {
                    // LibTiff's RGBA raster has its origin in the lower-left
                    // corner, so the top row of the tile is the last raster row.
                    var rasterRow = _tileHeight - 1 - (pixelY - tileY);
                    CopyRgbaRow(
                        raster,
                        rasterRow * _tileWidth + (left - tileX),
                        right - left,
                        destination,
                        ((pixelY - y) * width + (left - x)) * 4);
                }
            }
        }
    }

    private void ReadStripped(int x, int y, int width, int height, byte[] destination)
    {
        var rasterLength = (long)PixelWidth * _rowsPerStrip;
        var maximumRasterLength = MaximumStripRasterBytes / sizeof(int);
        if (rasterLength > maximumRasterLength)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF stores its pixels in a single strip that is too large for region reading.",
                _path,
                Path.GetExtension(_path));
        }

        var rasterLengthValue = (int)rasterLength;
        var firstStrip = y / _rowsPerStrip;
        var lastStrip = (y + height - 1) / _rowsPerStrip;
        for (var strip = firstStrip; strip <= lastStrip; strip++)
        {
            var stripRow = strip * _rowsPerStrip;
            var actualRows = Math.Min(_rowsPerStrip, PixelHeight - stripRow);
            if (!_stripCache.TryGetValue(strip, out var raster))
            {
                raster = GC.AllocateUninitializedArray<int>(rasterLengthValue);
                if (!_image.ReadRGBAStrip(stripRow, raster))
                {
                    throw new ImageLoadException(
                        ImageLoadFailure.Corrupt,
                        "The TIFF strip could not be decoded.",
                        _path,
                        Path.GetExtension(_path));
                }

                CacheStrip(strip, raster, rasterLengthValue);
            }

            var top = Math.Max(y, stripRow);
            var bottom = Math.Min(y + height, stripRow + actualRows);
            for (var pixelY = top; pixelY < bottom; pixelY++)
            {
                // The strip raster is bottom-up as well. A partial last strip
                // starts at raster row zero, so use the actual row count.
                var rasterRow = actualRows - 1 - (pixelY - stripRow);
                CopyRgbaRow(
                    raster,
                    rasterRow * PixelWidth + x,
                    width,
                    destination,
                    ((pixelY - y) * width) * 4);
            }
        }
    }

    private void CacheStrip(int strip, int[] raster, int rasterLength)
    {
        var bytes = (long)rasterLength * sizeof(int);
        if (bytes > MaximumStripCacheBytes)
        {
            return;
        }

        while (_stripCacheBytes + bytes > MaximumStripCacheBytes &&
               _stripOrder.First is { } first)
        {
            _stripOrder.RemoveFirst();
            if (_stripCache.Remove(first.Value, out var evicted))
            {
                _stripCacheBytes -= (long)evicted.Length * sizeof(int);
            }
        }

        _stripCache[strip] = raster;
        _stripOrder.AddLast(strip);
        _stripCacheBytes += bytes;
    }

    private static void CopyRgbaRow(
        int[] raster,
        int rasterOffset,
        int width,
        byte[] destination,
        int destinationOffset)
    {
        for (var index = 0; index < width; index++)
        {
            var value = raster[rasterOffset + index];
            // LibTiff's RGBA reader returns premultiplied colour samples for
            // associated and unassociated alpha, which is what Avalonia expects.
            destination[destinationOffset] = (byte)Tiff.GetB(value);
            destination[destinationOffset + 1] = (byte)Tiff.GetG(value);
            destination[destinationOffset + 2] = (byte)Tiff.GetR(value);
            destination[destinationOffset + 3] = (byte)Tiff.GetA(value);
            destinationOffset += 4;
        }
    }

    private static int TagInt(Tiff image, TiffTag tag)
    {
        var value = TagLong(image, tag);
        return value is <= 0 or > int.MaxValue ? 0 : (int)value;
    }

    private static long TagLong(Tiff image, TiffTag tag)
    {
        var field = image.GetField(tag);
        return field is { Length: > 0 } ? field[0].ToLong() : 0;
    }
}
