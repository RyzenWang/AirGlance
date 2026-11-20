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
    private readonly Dictionary<int, byte[]> _rawStripCache = [];
    private readonly LinkedList<int> _rawStripOrder = [];
    private readonly RegionRawLayout _rawLayout;
    private readonly int _rawPixelBytes;
    private readonly ushort[]? _rawPalette;
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

        _rawLayout = GetRawLayout(image);
        _rawPixelBytes = RawPixelBytes(_rawLayout);
        _rawPalette = _rawLayout == RegionRawLayout.Palette8 ? ReadColorMap(image) : null;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public event Action<double>? CacheProgressChanged;

    public bool RequiresStreamingCache => IsOversizedSingleStrip();

    public void Prepare(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOversizedSingleStrip())
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
        var cache = EnsureSingleStripCache();
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

    private uint ReadRawPixel(byte[] source, int offset)
    {
        switch (_rawLayout)
        {
            case RegionRawLayout.Rgb8:
                return Opaque(source[offset], source[offset + 1], source[offset + 2]);
            case RegionRawLayout.Rgb16:
                return Opaque(
                    (byte)(ReadUInt16Native(source, offset) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 2) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 4) >> 8));
            case RegionRawLayout.Cmyk8:
                var cyan = source[offset];
                var magenta = source[offset + 1];
                var yellow = source[offset + 2];
                var black = source[offset + 3];
                return Opaque(
                    (byte)(255 - Math.Min(255, cyan + black)),
                    (byte)(255 - Math.Min(255, magenta + black)),
                    (byte)(255 - Math.Min(255, yellow + black)));
            case RegionRawLayout.Rgba8:
                return PremultipliedBgra(
                    source[offset],
                    source[offset + 1],
                    source[offset + 2],
                    source[offset + 3]);
            case RegionRawLayout.Palette8:
                var palette = _rawPalette!;
                var index = source[offset];
                return Opaque(
                    (byte)(palette[index] >> 8),
                    (byte)(palette[256 + index] >> 8),
                    (byte)(palette[512 + index] >> 8));
            case RegionRawLayout.Gray8:
                return Opaque(source[offset], source[offset], source[offset]);
            case RegionRawLayout.Gray16:
                var gray16 = (byte)(ReadUInt16Native(source, offset) >> 8);
                return Opaque(gray16, gray16, gray16);
            case RegionRawLayout.Gray8Inverted:
                var inverted = (byte)(255 - source[offset]);
                return Opaque(inverted, inverted, inverted);
            case RegionRawLayout.Gray16Inverted:
                var inverted16 = (byte)(255 - (ReadUInt16Native(source, offset) >> 8));
                return Opaque(inverted16, inverted16, inverted16);
            default:
                return 0xFF000000u;
        }
    }

    private static uint Opaque(byte red, byte green, byte blue) =>
        0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;

    private static uint PremultipliedBgra(byte red, byte green, byte blue, byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        if (alpha != 255)
        {
            red = (byte)((red * alpha + 127) / 255);
            green = (byte)((green * alpha + 127) / 255);
            blue = (byte)((blue * alpha + 127) / 255);
        }

        return ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
    }

    private static void WriteBgra(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
        destination[offset + 2] = (byte)(value >> 16);
        destination[offset + 3] = (byte)(value >> 24);
    }

    private static RegionRawLayout GetRawLayout(Tiff image)
    {
        var bits = TagInt(image, TiffTag.BITSPERSAMPLE);
        if (bits is not (8 or 16))
        {
            return RegionRawLayout.None;
        }

        var samples = TagInt(image, TiffTag.SAMPLESPERPIXEL);
        if (samples == 0)
        {
            samples = 1;
        }

        var planar = TagInt(image, TiffTag.PLANARCONFIG);
        if (planar is not (0 or 1))
        {
            return RegionRawLayout.None;
        }

        var compression = TagInt(image, TiffTag.COMPRESSION);
        if (compression is not (1 or 5 or 7 or 8 or 32946 or 32773))
        {
            return RegionRawLayout.None;
        }

        if (bits == 16)
        {
            var sampleFormat = TagInt(image, TiffTag.SAMPLEFORMAT);
            if (sampleFormat is not (0 or 1))
            {
                return RegionRawLayout.None;
            }
        }

        var photometric = TagInt(image, TiffTag.PHOTOMETRIC);
        if (bits == 8 && samples == 3 && photometric == 6 && compression == 7)
        {
            EnableJpegRgbMode(image);
            return RegionRawLayout.Rgb8;
        }

        if (bits == 8 && samples == 4 && photometric == 5 && compression != 7)
        {
            return RegionRawLayout.Cmyk8;
        }

        if (bits == 8 && samples == 1 && photometric == 3 && compression != 7 &&
            ReadColorMap(image) is { Length: >= 768 })
        {
            return RegionRawLayout.Palette8;
        }

        if (bits == 8 && samples == 4 && photometric == 2 && compression != 7 &&
            image.GetField(TiffTag.EXTRASAMPLES) is not null)
        {
            return RegionRawLayout.Rgba8;
        }

        if (samples == 3 && photometric == 2)
        {
            return bits == 16 ? RegionRawLayout.Rgb16 : RegionRawLayout.Rgb8;
        }

        if (samples == 1 && photometric == 1)
        {
            return bits == 16 ? RegionRawLayout.Gray16 : RegionRawLayout.Gray8;
        }

        if (samples == 1 && photometric == 0)
        {
            return bits == 16 ? RegionRawLayout.Gray16Inverted : RegionRawLayout.Gray8Inverted;
        }

        return RegionRawLayout.None;
    }

    private static int RawPixelBytes(RegionRawLayout layout) =>
        layout switch
        {
            RegionRawLayout.None => 0,
            RegionRawLayout.Rgb8 => 3,
            RegionRawLayout.Rgb16 => 6,
            RegionRawLayout.Cmyk8 or RegionRawLayout.Rgba8 => 4,
            RegionRawLayout.Gray8 or RegionRawLayout.Gray8Inverted or RegionRawLayout.Palette8 => 1,
            _ => 2
        };

    private static ushort ReadUInt16Native(byte[] source, int offset) =>
        BitConverter.IsLittleEndian
            ? (ushort)(source[offset] | (source[offset + 1] << 8))
            : (ushort)((source[offset] << 8) | source[offset + 1]);

    private static void EnableJpegRgbMode(Tiff image)
    {
        if (TagInt(image, TiffTag.COMPRESSION) == 7 &&
            TagInt(image, TiffTag.PHOTOMETRIC) == 6 &&
            TagInt(image, TiffTag.SAMPLESPERPIXEL) == 3)
        {
            image.SetField(TiffTag.JPEGCOLORMODE, JpegColorMode.RGB);
        }
    }

    private static ushort[]? ReadColorMap(Tiff image)
    {
        var field = image.GetField(TiffTag.COLORMAP);
        if (field is null)
        {
            return null;
        }

        foreach (var value in field)
        {
            switch (value.Value)
            {
                case ushort[] map when map.Length >= 768:
                    return map;
                case int[] ints when ints.Length >= 768:
                {
                    var map = new ushort[ints.Length];
                    for (var index = 0; index < ints.Length; index++)
                    {
                        map[index] = (ushort)Math.Clamp(ints[index], 0, ushort.MaxValue);
                    }

                    return map;
                }
            }
        }

        return null;
    }

    private enum RegionRawLayout
    {
        None,
        Rgb8,
        Rgb16,
        Cmyk8,
        Rgba8,
        Palette8,
        Gray8,
        Gray16,
        Gray8Inverted,
        Gray16Inverted
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
