using System.Buffers.Binary;
using BitMiracle.LibTiff.Classic;
using static AirGlance.Core.Imaging.TiffCodec;
using RegionRawLayout = AirGlance.Core.Imaging.TiffRawLayout;

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
    private readonly Dictionary<int, byte[]> _rawStripCache = [];
    private readonly LinkedList<int> _rawStripOrder = [];
    private readonly RegionRawLayout _rawLayout;
    private readonly int _rawPixelBytes;
    private readonly ushort[]? _rawPalette;
    private readonly int _subX;
    private readonly int _subY;
    private int[] _tileRaster = [];
    private byte[] _rawTile = [];
    private FileStream? _streamCache;
    private long _stripCacheBytes;
    private long _rawStripCacheBytes;
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

        var format = TiffCodec.GetRawFormat(image);
        _rawLayout = format.Layout;
        _rawPixelBytes = RawPixelBytes(_rawLayout);
        _rawPalette = format.Palette;
        (_subX, _subY) = (format.SubX, format.SubY);
        (_subX, _subY) = ReadSubsampling(image);
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public event Action<double>? CacheProgressChanged;

    public bool RequiresStreamingCache =>
        _rawLayout is RegionRawLayout.YCbCrSub8 or RegionRawLayout.RgbPlanar8 or
            RegionRawLayout.RgbPlanar16 or RegionRawLayout.CmykPlanar8 or RegionRawLayout.CmykPlanar16 or
            RegionRawLayout.RgbaPlanar8 or RegionRawLayout.RgbaPlanar16 ||
        IsOversizedSingleStrip();

    public void Prepare(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_rawLayout == RegionRawLayout.YCbCrSub8)
            {
                EnsureYCbCrSubsampledCache(cancellationToken);
            }
            else if (_rawLayout is RegionRawLayout.RgbPlanar8 or RegionRawLayout.RgbPlanar16 or
                     RegionRawLayout.CmykPlanar8 or RegionRawLayout.CmykPlanar16 or RegionRawLayout.RgbaPlanar8 or RegionRawLayout.RgbaPlanar16)
            {
                EnsurePlanarCache(cancellationToken);
            }
            else if (IsOversizedSingleStrip())
            {
                EnsureSingleStripCache(cancellationToken);
            }
        }
    }

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
            _streamCache?.Dispose();
            _streamCache = null;
            _stripCache.Clear();
            _stripOrder.Clear();
            _rawStripCache.Clear();
            _rawStripOrder.Clear();
            _stripCacheBytes = 0;
            _rawStripCacheBytes = 0;
        }
    }

    private void ReadTiled(int x, int y, int width, int height, byte[] destination)
    {
        if (_rawLayout is RegionRawLayout.RgbPlanar8 or RegionRawLayout.RgbPlanar16 or
            RegionRawLayout.CmykPlanar8 or RegionRawLayout.CmykPlanar16 or RegionRawLayout.RgbaPlanar8 or RegionRawLayout.RgbaPlanar16)
        {
            ReadSingleStripStreaming(x, y, width, height, destination);
            return;
        }

        if (_rawLayout != RegionRawLayout.None)
        {
            ReadTiledRaw(x, y, width, height, destination);
            return;
        }

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
        if (_rawLayout is RegionRawLayout.RgbPlanar8 or RegionRawLayout.RgbPlanar16 or
            RegionRawLayout.CmykPlanar8 or RegionRawLayout.CmykPlanar16 or RegionRawLayout.RgbaPlanar8 or RegionRawLayout.RgbaPlanar16)
        {
            ReadSingleStripStreaming(x, y, width, height, destination);
            return;
        }

        if (_rawLayout == RegionRawLayout.YCbCrSub8)
        {
            ReadSingleStripStreaming(x, y, width, height, destination);
            return;
        }

        if (_rawLayout != RegionRawLayout.None)
        {
            if (IsOversizedSingleStrip())
            {
                ReadSingleStripStreaming(x, y, width, height, destination);
                return;
            }

            ReadStrippedRaw(x, y, width, height, destination);
            return;
        }

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

    private void ReadTiledRaw(int x, int y, int width, int height, byte[] destination)
    {
        var tileSize = _image.TileSize();
        if (tileSize <= 0 || tileSize > MaximumStripRasterBytes)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF tile is too large for region reading.",
                _path,
                Path.GetExtension(_path));
        }

        if (_rawTile.Length < tileSize)
        {
            _rawTile = new byte[tileSize];
        }

        var raw = _rawTile;
        var tilesAcross = (PixelWidth + _tileWidth - 1) / _tileWidth;
        var endX = x + width;
        var endY = y + height;
        var firstTileX = x / _tileWidth * _tileWidth;
        var firstTileY = y / _tileHeight * _tileHeight;
        for (var tileY = firstTileY; tileY < endY; tileY += _tileHeight)
        {
            for (var tileX = firstTileX; tileX < endX; tileX += _tileWidth)
            {
                var tileIndex = (tileY / _tileHeight) * tilesAcross + (tileX / _tileWidth);
                var read = _image.ReadEncodedTile(tileIndex, raw, 0, tileSize);
                if (read <= 0)
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
                    var sourceRow = pixelY - tileY;
                    for (var pixelX = left; pixelX < right; pixelX++)
                    {
                        var offset = (sourceRow * _tileWidth + pixelX - tileX) * _rawPixelBytes;
                        if (offset + _rawPixelBytes > read)
                        {
                            continue;
                        }

                        var target = ((pixelY - y) * width + pixelX - x) * 4;
                        WriteBgra(destination, target, ReadRawPixel(raw, offset));
                    }
                }
            }
        }
    }

    private void ReadStrippedRaw(int x, int y, int width, int height, byte[] destination)
    {
        var stripSize = _image.StripSize();
        if (stripSize <= 0 || stripSize > MaximumStripRasterBytes)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF strip is too large for region reading.",
                _path,
                Path.GetExtension(_path));
        }

        var firstStrip = y / _rowsPerStrip;
        var lastStrip = (y + height - 1) / _rowsPerStrip;
        for (var strip = firstStrip; strip <= lastStrip; strip++)
        {
            var stripRow = strip * _rowsPerStrip;
            var actualRows = Math.Min(_rowsPerStrip, PixelHeight - stripRow);
            int read;
            if (!_rawStripCache.TryGetValue(strip, out var raw))
            {
                raw = new byte[stripSize];
                read = _image.ReadEncodedStrip(strip, raw, 0, stripSize);
                if (read <= 0)
                {
                    throw new ImageLoadException(
                        ImageLoadFailure.Corrupt,
                        "The TIFF strip could not be decoded.",
                        _path,
                        Path.GetExtension(_path));
                }

                CacheRawStrip(strip, raw);
            }
            else
            {
                read = PixelWidth * actualRows * _rawPixelBytes;
            }

            var top = Math.Max(y, stripRow);
            var bottom = Math.Min(y + height, stripRow + actualRows);
            for (var pixelY = top; pixelY < bottom; pixelY++)
            {
                var sourceRow = pixelY - stripRow;
                for (var pixelX = x; pixelX < x + width; pixelX++)
                {
                    var offset = (sourceRow * PixelWidth + pixelX) * _rawPixelBytes;
                    if (offset + _rawPixelBytes > read)
                    {
                        continue;
                    }

                    var target = ((pixelY - y) * width + pixelX - x) * 4;
                    WriteBgra(destination, target, ReadRawPixel(raw, offset));
                }
            }
        }
    }

    private bool IsOversizedSingleStrip() =>
        !_tiled &&
        _rowsPerStrip >= PixelHeight &&
        (long)PixelWidth * PixelHeight * _rawPixelBytes > MaximumStripRasterBytes;

    private void ReadSingleStripStreaming(int x, int y, int width, int height, byte[] destination)
    {
        var cache = _rawLayout switch
        {
            RegionRawLayout.YCbCrSub8 => EnsureYCbCrSubsampledCache(),
            RegionRawLayout.RgbPlanar8 or RegionRawLayout.RgbPlanar16 or
                RegionRawLayout.CmykPlanar8 or RegionRawLayout.CmykPlanar16 or
                RegionRawLayout.RgbaPlanar8 or RegionRawLayout.RgbaPlanar16 => EnsurePlanarCache(),
            _ => EnsureSingleStripCache()
        };
        var rowBytes = width * 4;
        var row = new byte[rowBytes];
        try
        {
            for (var pixelY = y; pixelY < y + height; pixelY++)
            {
                cache.Position = ((long)pixelY * PixelWidth + x) * 4;
                cache.ReadExactly(row);
                Buffer.BlockCopy(row, 0, destination, (pixelY - y) * rowBytes, rowBytes);
            }
        }
        catch (EndOfStreamException)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The single-strip cache ended unexpectedly.",
                _path,
                Path.GetExtension(_path));
        }
    }

    private void WritePlanarTiledCache(FileStream cache, CancellationToken cancellationToken)
    {
        var tileWidth = TagInt(_image, TiffTag.TILEWIDTH);
        var tileHeight = TagInt(_image, TiffTag.TILELENGTH);
        var tileSize = _image.TileSize();
        if (tileWidth <= 0 || tileHeight <= 0 || tileSize <= 0 || tileSize > MaximumStripRasterBytes)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The planar TIFF tile is too large for region reading.",
                _path,
                Path.GetExtension(_path));
        }

        var wide = _rawLayout is RegionRawLayout.RgbPlanar16 or RegionRawLayout.RgbaPlanar16;
        var bytesPerSample = wide ? 2 : 1;
        var planeCount = _rawLayout is RegionRawLayout.CmykPlanar8 or RegionRawLayout.RgbaPlanar8 or
            RegionRawLayout.RgbaPlanar16
            ? 4
            : 3;
        var planes = new byte[planeCount][];
        for (var plane = 0; plane < planeCount; plane++)
        {
            planes[plane] = new byte[tileSize];
        }

        var across = (PixelWidth + tileWidth - 1) / tileWidth;
        var tilesPerPlane = across * ((PixelHeight + tileHeight - 1) / tileHeight);
        var band = new byte[tileHeight * PixelWidth * 4];
        var reportEvery = Math.Max(1, PixelHeight / 100);
        var rowsWritten = 0;
        for (var tileY = 0; tileY < PixelHeight; tileY += tileHeight)
        {
            Array.Clear(band);
            var rows = Math.Min(tileHeight, PixelHeight - tileY);
            for (var tileX = 0; tileX < PixelWidth; tileX += tileWidth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tileIndex = (tileY / tileHeight) * across + tileX / tileWidth;
                for (var plane = 0; plane < planeCount; plane++)
                {
                    if (_image.ReadEncodedTile(plane * tilesPerPlane + tileIndex, planes[plane], 0, tileSize) <= 0)
                    {
                        throw new ImageLoadException(
                            ImageLoadFailure.Corrupt,
                            "The planar TIFF tile could not be decoded.",
                            _path,
                            Path.GetExtension(_path));
                    }
                }

                var columns = Math.Min(tileWidth, PixelWidth - tileX);
                for (var localY = 0; localY < rows; localY++)
                {
                    for (var localX = 0; localX < columns; localX++)
                    {
                        var sampleOffset = (localY * tileWidth + localX) * bytesPerSample;
                        byte red;
                        byte green;
                        byte blue;
                        byte alpha = 255;
                        if (wide)
                        {
                            if (_rawLayout == RegionRawLayout.CmykPlanar16)
                            {
                                var c16 = (byte)(ReadUInt16Native(planes[0], sampleOffset) >> 8);
                                var m16 = (byte)(ReadUInt16Native(planes[1], sampleOffset) >> 8);
                                var y16 = (byte)(ReadUInt16Native(planes[2], sampleOffset) >> 8);
                                var k16 = (byte)(ReadUInt16Native(planes[3], sampleOffset) >> 8);
                                red = (byte)(255 - Math.Min(255, c16 + k16));
                                green = (byte)(255 - Math.Min(255, m16 + k16));
                                blue = (byte)(255 - Math.Min(255, y16 + k16));
                            }
                            else
                            {
                                red = (byte)(ReadUInt16Native(planes[0], sampleOffset) >> 8);
                                green = (byte)(ReadUInt16Native(planes[1], sampleOffset) >> 8);
                                blue = (byte)(ReadUInt16Native(planes[2], sampleOffset) >> 8);
                                if (_rawLayout == RegionRawLayout.RgbaPlanar16)
                                {
                                    alpha = (byte)(ReadUInt16Native(planes[3], sampleOffset) >> 8);
                                }
                            }
                        }
                        else if (_rawLayout == RegionRawLayout.CmykPlanar8)
                        {
                            red = (byte)(255 - Math.Min(255, planes[0][sampleOffset] + planes[3][sampleOffset]));
                            green = (byte)(255 - Math.Min(255, planes[1][sampleOffset] + planes[3][sampleOffset]));
                            blue = (byte)(255 - Math.Min(255, planes[2][sampleOffset] + planes[3][sampleOffset]));
                        }
                        else if (_rawLayout == RegionRawLayout.RgbaPlanar8)
                        {
                            alpha = planes[3][sampleOffset];
                            red = planes[0][sampleOffset];
                            green = planes[1][sampleOffset];
                            blue = planes[2][sampleOffset];
                        }
                        else
                        {
                            red = planes[0][sampleOffset];
                            green = planes[1][sampleOffset];
                            blue = planes[2][sampleOffset];
                        }

                        WriteBgra(
                            band,
                            (localY * PixelWidth + tileX + localX) * 4,
                            alpha == 255
                                ? Opaque(red, green, blue)
                                : PremultipliedBgra(red, green, blue, alpha));
                    }
                }
            }

            cache.Write(band, 0, rows * PixelWidth * 4);
            rowsWritten += rows;
            if (rowsWritten % reportEvery == 0 || rowsWritten == PixelHeight)
            {
                CacheProgressChanged?.Invoke(rowsWritten / (double)PixelHeight);
            }
        }
    }

    private FileStream EnsurePlanarCache(CancellationToken cancellationToken = default)
    {
        if (_streamCache is not null)
        {
            return _streamCache;
        }

        var cachePath = Path.Combine(
            Path.GetTempPath(),
            $"airglance-region-{Guid.NewGuid():N}.bgra");
        var cache = new FileStream(
            cachePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1 << 20,
            FileOptions.RandomAccess | FileOptions.DeleteOnClose);
        try
        {
            if (_image.IsTiled())
            {
                WritePlanarTiledCache(cache, cancellationToken);
                cache.Flush();
                CacheProgressChanged?.Invoke(1);
                _streamCache = cache;
                return cache;
            }

            var rowsPerStrip = TagLong(_image, TiffTag.ROWSPERSTRIP);
            if (rowsPerStrip <= 0 || rowsPerStrip > PixelHeight)
            {
                rowsPerStrip = PixelHeight;
            }

            var stripsPerPlane = (PixelHeight + (int)rowsPerStrip - 1) / (int)rowsPerStrip;
            var wide = _rawLayout is RegionRawLayout.RgbPlanar16 or RegionRawLayout.RgbaPlanar16 or RegionRawLayout.CmykPlanar16;
            var bytesPerSample = wide ? 2 : 1;
            var planeSize = checked(PixelWidth * (int)rowsPerStrip * bytesPerSample);
            if (planeSize > MaximumStripRasterBytes)
            {
                throw new ImageLoadException(
                    ImageLoadFailure.TooLarge,
                    "The planar TIFF strip is too large for region reading.",
                    _path,
                    Path.GetExtension(_path));
            }

            var planeCount = _rawLayout is RegionRawLayout.CmykPlanar8 or RegionRawLayout.CmykPlanar16 or
                RegionRawLayout.RgbaPlanar8 or RegionRawLayout.RgbaPlanar16
                ? 4
                : 3;
            var planes = new byte[planeCount][];
            for (var plane = 0; plane < planeCount; plane++)
            {
                planes[plane] = new byte[planeSize];
            }

            var bgra = new byte[PixelWidth * 4];
            var reportEvery = Math.Max(1, PixelHeight / 100);
            var rowsWritten = 0;
            for (var strip = 0; strip < stripsPerPlane; strip++)
            {
                var stripRow = strip * (int)rowsPerStrip;
                var rows = Math.Min((int)rowsPerStrip, PixelHeight - stripRow);
                for (var plane = 0; plane < planeCount; plane++)
                {
                    if (_image.ReadEncodedStrip(plane * stripsPerPlane + strip, planes[plane], 0, planeSize) <= 0)
                    {
                        throw new ImageLoadException(
                            ImageLoadFailure.Corrupt,
                            "The planar TIFF strip could not be decoded.",
                            _path,
                            Path.GetExtension(_path));
                    }
                }

                for (var localRow = 0; localRow < rows; localRow++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rowOffset = localRow * PixelWidth * bytesPerSample;
                    for (var pixelX = 0; pixelX < PixelWidth; pixelX++)
                    {
                        var sampleOffset = rowOffset + pixelX * bytesPerSample;
                        byte red;
                        byte green;
                        byte blue;
                        byte alpha = 255;
                        if (wide)
                        {
                            if (_rawLayout == RegionRawLayout.CmykPlanar16)
                            {
                                var c16 = (byte)(ReadUInt16Native(planes[0], sampleOffset) >> 8);
                                var m16 = (byte)(ReadUInt16Native(planes[1], sampleOffset) >> 8);
                                var y16 = (byte)(ReadUInt16Native(planes[2], sampleOffset) >> 8);
                                var k16 = (byte)(ReadUInt16Native(planes[3], sampleOffset) >> 8);
                                red = (byte)(255 - Math.Min(255, c16 + k16));
                                green = (byte)(255 - Math.Min(255, m16 + k16));
                                blue = (byte)(255 - Math.Min(255, y16 + k16));
                            }
                            else
                            {
                                red = (byte)(ReadUInt16Native(planes[0], sampleOffset) >> 8);
                                green = (byte)(ReadUInt16Native(planes[1], sampleOffset) >> 8);
                                blue = (byte)(ReadUInt16Native(planes[2], sampleOffset) >> 8);
                                if (_rawLayout == RegionRawLayout.RgbaPlanar16)
                                {
                                    alpha = (byte)(ReadUInt16Native(planes[3], sampleOffset) >> 8);
                                }
                            }
                        }
                        else if (_rawLayout == RegionRawLayout.CmykPlanar8)
                        {
                            red = (byte)(255 - Math.Min(255, planes[0][sampleOffset] + planes[3][sampleOffset]));
                            green = (byte)(255 - Math.Min(255, planes[1][sampleOffset] + planes[3][sampleOffset]));
                            blue = (byte)(255 - Math.Min(255, planes[2][sampleOffset] + planes[3][sampleOffset]));
                        }
                        else if (_rawLayout == RegionRawLayout.RgbaPlanar8)
                        {
                            alpha = planes[3][sampleOffset];
                            red = planes[0][sampleOffset];
                            green = planes[1][sampleOffset];
                            blue = planes[2][sampleOffset];
                        }
                        else
                        {
                            red = planes[0][sampleOffset];
                            green = planes[1][sampleOffset];
                            blue = planes[2][sampleOffset];
                        }

                        var value = alpha == 255
                            ? Opaque(red, green, blue)
                            : PremultipliedBgra(red, green, blue, alpha);
                        WriteBgra(bgra, pixelX * 4, value);
                    }

                    cache.Write(bgra, 0, bgra.Length);
                    rowsWritten++;
                    if (rowsWritten % reportEvery == 0 || rowsWritten == PixelHeight)
                    {
                        CacheProgressChanged?.Invoke(rowsWritten / (double)PixelHeight);
                    }
                }
            }

            cache.Flush();
            CacheProgressChanged?.Invoke(1);
            _streamCache = cache;
            return cache;
        }
        catch
        {
            cache.Dispose();
            throw;
        }
    }

    private FileStream EnsureYCbCrSubsampledCache(CancellationToken cancellationToken = default)
    {
        if (_streamCache is not null)
        {
            return _streamCache;
        }

        var offsets = ReadIfdTagValues(_path, TiffTag.STRIPOFFSETS);
        if (offsets.Length == 0)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The TIFF has no strip offsets.",
                _path,
                Path.GetExtension(_path));
        }

        var rowsPerStripValues = ReadIfdTagValues(_path, TiffTag.ROWSPERSTRIP);
        var rowsPerStrip = rowsPerStripValues.Length > 0 ? rowsPerStripValues[0] : PixelHeight;
        if (rowsPerStrip <= 0 || rowsPerStrip > PixelHeight)
        {
            rowsPerStrip = PixelHeight;
        }

        var subX = Math.Max(1, _subX);
        var subY = Math.Max(1, _subY);
        var blocksAcross = (PixelWidth + subX - 1) / subX;
        var blockBytes = subX * subY + 2;
        var blockRowBytes = blocksAcross * blockBytes;
        var cachePath = Path.Combine(
            Path.GetTempPath(),
            $"airglance-region-{Guid.NewGuid():N}.bgra");
        var cache = new FileStream(
            cachePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1 << 20,
            FileOptions.RandomAccess | FileOptions.DeleteOnClose);
        try
        {
            var bgra = new byte[PixelWidth * 4];
            var reportEvery = Math.Max(1, PixelHeight / 100);
            var rowsWritten = 0;
            if (TagInt(_image, TiffTag.COMPRESSION) != 1)
            {
                var stripSize = _image.StripSize();
                if (stripSize <= 0 || stripSize > MaximumStripRasterBytes)
                {
                    throw new ImageLoadException(
                        ImageLoadFailure.TooLarge,
                        "The compressed YCbCr strip is too large for region reading.",
                        _path,
                        Path.GetExtension(_path));
                }

                var strip = new byte[stripSize];
                for (var stripIndex = 0; stripIndex < offsets.Length; stripIndex++)
                {
                    var stripRow = stripIndex * (int)rowsPerStrip;
                    if (stripRow >= PixelHeight)
                    {
                        break;
                    }

                    var rowCount = Math.Min((int)rowsPerStrip, PixelHeight - stripRow);
                    var read = _image.ReadEncodedStrip(stripIndex, strip, 0, strip.Length);
                    if (read <= 0)
                    {
                        throw new ImageLoadException(
                            ImageLoadFailure.Corrupt,
                            "The compressed YCbCr strip could not be decoded.",
                            _path,
                            Path.GetExtension(_path));
                    }

                    WriteYCbCrStripCache(
                        strip,
                        read,
                        stripRow,
                        rowCount,
                        subX,
                        subY,
                        bgra,
                        cache,
                        ref rowsWritten,
                        reportEvery,
                        cancellationToken);
                }

                cache.Flush();
                CacheProgressChanged?.Invoke(1);
                _streamCache = cache;
                return cache;
            }

            using var source = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var blockRow = new byte[blockRowBytes];
            for (var strip = 0; strip < offsets.Length; strip++)
            {
                var stripRow = strip * (int)rowsPerStrip;
                if (stripRow >= PixelHeight)
                {
                    break;
                }

                var rowCount = Math.Min((int)rowsPerStrip, PixelHeight - stripRow);
                source.Position = offsets[strip];
                var blockRows = (rowCount + subY - 1) / subY;
                for (var blockRowIndex = 0; blockRowIndex < blockRows; blockRowIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        source.ReadExactly(blockRow);
                    }
                    catch (EndOfStreamException)
                    {
                        throw new ImageLoadException(
                            ImageLoadFailure.Corrupt,
                            "The TIFF YCbCr strip ended unexpectedly.",
                            _path,
                            Path.GetExtension(_path));
                    }

                    for (var dy = 0; dy < subY; dy++)
                    {
                        var pixelY = stripRow + blockRowIndex * subY + dy;
                        if (pixelY >= PixelHeight)
                        {
                            break;
                        }

                        for (var pixelX = 0; pixelX < PixelWidth; pixelX++)
                        {
                            var blockX = pixelX / subX;
                            var withinX = pixelX % subX;
                            var blockOffset = blockX * blockBytes;
                            var value = FromYCbCr(
                                blockRow[blockOffset + dy * subX + withinX],
                                blockRow[blockOffset + subX * subY],
                                blockRow[blockOffset + subX * subY + 1]);
                            WriteBgra(bgra, pixelX * 4, value);
                        }

                        cache.Write(bgra, 0, bgra.Length);
                        rowsWritten++;
                        if (rowsWritten % reportEvery == 0 || rowsWritten == PixelHeight)
                        {
                            CacheProgressChanged?.Invoke(rowsWritten / (double)PixelHeight);
                        }
                    }
                }
            }

            cache.Flush();
            CacheProgressChanged?.Invoke(1);
            _streamCache = cache;
            return cache;
        }
        catch
        {
            cache.Dispose();
            throw;
        }
    }

    private void WriteYCbCrStripCache(
        byte[] strip,
        int read,
        int stripRow,
        int rowCount,
        int subX,
        int subY,
        byte[] bgra,
        FileStream cache,
        ref int rowsWritten,
        int reportEvery,
        CancellationToken cancellationToken)
    {
        var blocksAcross = (PixelWidth + subX - 1) / subX;
        var blockBytes = subX * subY + 2;
        var blockRowBytes = blocksAcross * blockBytes;
        var blockRows = (rowCount + subY - 1) / subY;
        for (var blockRowIndex = 0; blockRowIndex < blockRows; blockRowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blockRowOffset = blockRowIndex * blockRowBytes;
            for (var dy = 0; dy < subY; dy++)
            {
                var pixelY = stripRow + blockRowIndex * subY + dy;
                if (pixelY >= PixelHeight)
                {
                    break;
                }

                for (var pixelX = 0; pixelX < PixelWidth; pixelX++)
                {
                    var blockOffset = blockRowOffset + (pixelX / subX) * blockBytes;
                    var yOffset = blockOffset + dy * subX + (pixelX % subX);
                    var cbOffset = blockOffset + subX * subY;
                    if (cbOffset + 1 >= read || cbOffset + 1 >= strip.Length)
                    {
                        continue;
                    }

                    WriteBgra(
                        bgra,
                        pixelX * 4,
                        FromYCbCr(strip[yOffset], strip[cbOffset], strip[cbOffset + 1]));
                }

                cache.Write(bgra, 0, bgra.Length);
                rowsWritten++;
                if (rowsWritten % reportEvery == 0 || rowsWritten == PixelHeight)
                {
                    CacheProgressChanged?.Invoke(rowsWritten / (double)PixelHeight);
                }
            }
        }
    }

    private static long[] ReadIfdTagValues(string path, TiffTag tag)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < 8)
            {
                return [];
            }

            Span<byte> header = stackalloc byte[16];
            var headerLength = (int)Math.Min(header.Length, stream.Length);
            stream.ReadExactly(header[..headerLength]);
            var little = header[0] == (byte)'I' && header[1] == (byte)'I';
            var bigEndian = header[0] == (byte)'M' && header[1] == (byte)'M';
            if (!little && !bigEndian)
            {
                return [];
            }

            var magic = ReadU16(header[2..4], little);
            var bigTiff = magic == 43;
            long ifdOffset;
            if (magic == 42)
            {
                ifdOffset = ReadU32(header[4..8], little);
            }
            else if (bigTiff && headerLength >= 16)
            {
                ifdOffset = (long)ReadU64(header[8..16], little);
            }
            else
            {
                return [];
            }

            var countSize = bigTiff ? 8 : 2;
            var entrySize = bigTiff ? 20 : 12;
            var offsetSize = bigTiff ? 8 : 4;
            if (ifdOffset <= 0 || ifdOffset + countSize > stream.Length)
            {
                return [];
            }

            stream.Position = ifdOffset;
            var entryCount = bigTiff ? ReadU64(stream, little) : ReadU16(stream, little);
            if (entryCount is 0 or > 4096)
            {
                return [];
            }

            var entryBase = ifdOffset + countSize;
            for (ulong index = 0; index < entryCount; index++)
            {
                var position = entryBase + (long)index * entrySize;
                if (position + entrySize > stream.Length)
                {
                    return [];
                }

                stream.Position = position;
                var entryTag = ReadU16(stream, little);
                var type = ReadU16(stream, little);
                var count = bigTiff ? ReadU64(stream, little) : ReadU32(stream, little);
                var valuePosition = stream.Position;
                if (entryTag != (ushort)tag || count is 0 or > 100_000)
                {
                    continue;
                }

                var itemSize = type switch
                {
                    3 => 2,
                    4 or 13 => 4,
                    16 or 18 => 8,
                    _ => 0
                };
                if (itemSize == 0)
                {
                    return [];
                }

                var total = (long)count * itemSize;
                if (total > offsetSize)
                {
                    stream.Position = valuePosition;
                    var dataOffset = bigTiff
                        ? (long)ReadU64(stream, little)
                        : ReadU32(stream, little);
                    if (dataOffset <= 0 || dataOffset + total > stream.Length)
                    {
                        return [];
                    }

                    stream.Position = dataOffset;
                }
                else
                {
                    stream.Position = valuePosition;
                }

                var result = new long[count];
                for (var item = 0; item < result.Length; item++)
                {
                    result[item] = type switch
                    {
                        3 => ReadU16(stream, little),
                        4 or 13 => bigTiff && type == 13
                            ? (long)ReadU64(stream, little)
                            : ReadU32(stream, little),
                        _ => (long)ReadU64(stream, little)
                    };
                }

                return result;
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ArgumentException)
        {
            // The raw IFD is unavailable; the caller can fall back.
        }

        return [];
    }







    private FileStream EnsureSingleStripCache(CancellationToken cancellationToken = default)
    {
        if (_streamCache is not null)
        {
            return _streamCache;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"airglance-region-{Guid.NewGuid():N}.bgra");
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1 << 20,
            FileOptions.RandomAccess | FileOptions.DeleteOnClose);
        try
        {
            var source = new byte[checked(PixelWidth * _rawPixelBytes)];
            var bgra = new byte[checked(PixelWidth * 4)];
            var reportEvery = Math.Max(1, PixelHeight / 100);
            for (var pixelY = 0; pixelY < PixelHeight; pixelY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_image.ReadScanline(source, pixelY))
                {
                    throw new ImageLoadException(
                        ImageLoadFailure.Corrupt,
                        "The single-strip TIFF could not be decoded.",
                        _path,
                        Path.GetExtension(_path));
                }

                for (var pixelX = 0; pixelX < PixelWidth; pixelX++)
                {
                    var target = pixelX * 4;
                    WriteBgra(bgra, target, ReadRawPixel(source, pixelX * _rawPixelBytes));
                }

                stream.Write(bgra, 0, bgra.Length);
                if (pixelY % reportEvery == 0 || pixelY == PixelHeight - 1)
                {
                    CacheProgressChanged?.Invoke((pixelY + 1) / (double)PixelHeight);
                }
            }

            stream.Flush();
            CacheProgressChanged?.Invoke(1);
            _streamCache = stream;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void CacheRawStrip(int strip, byte[] raw)
    {
        var bytes = (long)raw.Length;
        if (bytes > MaximumStripCacheBytes)
        {
            return;
        }

        while (_rawStripCacheBytes + bytes > MaximumStripCacheBytes &&
               _rawStripOrder.First is { } first)
        {
            _rawStripOrder.RemoveFirst();
            if (_rawStripCache.Remove(first.Value, out var evicted))
            {
                _rawStripCacheBytes -= evicted.Length;
            }
        }

        _rawStripCache[strip] = raw;
        _rawStripOrder.AddLast(strip);
        _rawStripCacheBytes += bytes;
    }



















    private uint ReadRawPixel(byte[] source, int offset) =>
        TiffCodec.ReadRawPixel(source, offset, new TiffRawFormat(_rawLayout, _rawPalette));

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


}
