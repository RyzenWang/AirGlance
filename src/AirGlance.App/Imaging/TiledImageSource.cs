using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AirGlance.App.Imaging;

/// <summary>
/// A file-backed tile store for very large images. The full-resolution pixels are
/// written to a temporary file once, then the source pixel buffer can be released.
/// Only the tiles touching the viewport are read back, so a 100 MP image does not
/// keep two full-size BGRA buffers alive for the lifetime of the window.
/// </summary>
public sealed class TiledImageSource : IDisposable
{
    public const int TileSize = 512;

    private const int HeaderSize = 64;
    private const int MaxCachedTiles = 32;
    private const uint Magic = 0x4C544741; // AGTL

    private readonly string? _path;
    private readonly FileStream? _stream;
    private readonly TiffRegionReader? _region;
    private readonly SemaphoreSlim _regionGate = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _pendingRegionTiles = [];
    private readonly Dictionary<int, WriteableBitmap> _cache = [];
    private readonly Queue<int> _cacheOrder = [];
    private readonly long _tileBytes;
    private bool _disposed;

    public event EventHandler? TileAvailable;

    private TiledImageSource(
        string path,
        FileStream stream,
        int pixelWidth,
        int pixelHeight)
        : this(pixelWidth, pixelHeight, path, stream, null)
    {
    }

    private TiledImageSource(TiffRegionReader region, int pixelWidth, int pixelHeight)
        : this(pixelWidth, pixelHeight, null, null, region)
    {
    }

    private TiledImageSource(
        int pixelWidth,
        int pixelHeight,
        string? path,
        FileStream? stream,
        TiffRegionReader? region)
    {
        _path = path;
        _stream = stream;
        _region = region;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        TileColumns = (pixelWidth + TileSize - 1) / TileSize;
        TileRows = (pixelHeight + TileSize - 1) / TileSize;
        _tileBytes = (long)TileSize * TileSize * 4;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int TileColumns { get; }

    public int TileRows { get; }

    public long FileSize => _region is not null
        ? 0
        : HeaderSize + (long)TileColumns * TileRows * _tileBytes;

    public static TiledImageSource OpenRegion(string path, int pixelWidth, int pixelHeight)
    {
        var region = TiffRegionReader.Open(path);
        return new TiledImageSource(region, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Writes the full-resolution pixels to a temporary tile file. Call from a
    /// background thread; the returned source owns the file until disposed.
    /// </summary>
    public static TiledImageSource Create(DecodedBitmap full, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(full);
        if (full.Width != full.SourceWidth || full.Height != full.SourceHeight)
        {
            throw new ArgumentException("The tile source requires full-resolution pixels.", nameof(full));
        }

        var directory = Path.Combine(Path.GetTempPath(), "AirGlance", "tiles");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.tiles");
        var tileColumns = (full.Width + TileSize - 1) / TileSize;
        var tileRows = (full.Height + TileSize - 1) / TileSize;
        var tileBytes = (long)TileSize * TileSize * 4;
        var expectedSize = HeaderSize + (long)tileColumns * tileRows * tileBytes;

        var driveRoot = Path.GetPathRoot(directory);
        try
        {
            if (!string.IsNullOrEmpty(driveRoot) &&
                new DriveInfo(driveRoot).AvailableFreeSpace < expectedSize + 64L * 1024 * 1024)
            {
                throw new IOException("Not enough temporary disk space for the tile cache.");
            }
        }
        catch (ArgumentException)
        {
            // A non-drive temp root (for example a UNC path) is allowed; the write
            // itself will report a real disk-full failure.
        }

        try
        {
            WriteTileFile(path, full, tileColumns, tileRows, cancellationToken);
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 20,
                FileOptions.RandomAccess);
            return new TiledImageSource(path, stream, full.Width, full.Height);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public static Task<TiledImageSource> CreateAsync(
        DecodedBitmap full,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(full, cancellationToken), cancellationToken);

    /// <summary>
    /// Creates the tile file directly from an unmanaged full-resolution decode. The
    /// raw buffer is oriented while it is copied into tiles, then the caller can free
    /// it without ever allocating a full-size managed array or Avalonia bitmap.
    /// </summary>
    public static TiledImageSource Create(
        DecodedImageBuffer raw,
        CancellationToken cancellationToken = default) =>
        Create(raw, EditTransform.None, cancellationToken);

    public static TiledImageSource Create(
        DecodedImageBuffer raw,
        EditTransform edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ObjectDisposedException.ThrowIf(raw.IsDisposed, raw);

        var destinationWidth = edit.SwapsAxes ? raw.SourceHeight : raw.SourceWidth;
        var destinationHeight = edit.SwapsAxes ? raw.SourceWidth : raw.SourceHeight;
        var directory = Path.Combine(Path.GetTempPath(), "AirGlance", "tiles");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.tiles");
        var tileColumns = (destinationWidth + TileSize - 1) / TileSize;
        var tileRows = (destinationHeight + TileSize - 1) / TileSize;
        var tileBytes = (long)TileSize * TileSize * 4;
        var expectedSize = HeaderSize + (long)tileColumns * tileRows * tileBytes;

        var driveRoot = Path.GetPathRoot(directory);
        try
        {
            if (!string.IsNullOrEmpty(driveRoot) &&
                new DriveInfo(driveRoot).AvailableFreeSpace < expectedSize + 64L * 1024 * 1024)
            {
                throw new IOException("Not enough temporary disk space for the tile cache.");
            }
        }
        catch (ArgumentException)
        {
            // A non-drive temp root (for example a UNC path) is allowed.
        }

        try
        {
            WriteTileFileRaw(
                path,
                raw,
                destinationWidth,
                destinationHeight,
                tileColumns,
                tileRows,
                edit,
                cancellationToken);
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 20,
                FileOptions.RandomAccess);
            return new TiledImageSource(path, stream, destinationWidth, destinationHeight);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public static Task<TiledImageSource> CreateAsync(
        DecodedImageBuffer raw,
        CancellationToken cancellationToken = default) =>
        CreateAsync(raw, EditTransform.None, cancellationToken);

    public static Task<TiledImageSource> CreateAsync(
        DecodedImageBuffer raw,
        EditTransform edit,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(raw, edit, cancellationToken), cancellationToken);

    /// <summary>
    /// Removes tile files abandoned by a crash. Files in active use are much newer
    /// than the default one-day cutoff.
    /// </summary>
    public static void CleanupStale(TimeSpan maxAge)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "AirGlance", "tiles");
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(directory, "*.tiles"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // A file another instance owns can wait for the next run.
                }
            }
        }
        catch
        {
            // Cleanup is best effort and must never block startup.
        }
    }

    /// <summary>
    /// Returns a tile bitmap, loading it from the tile file on first use. Must be
    /// called from the UI thread; the small LRU keeps memory bounded while panning.
    /// </summary>
    public WriteableBitmap? GetTile(int column, int row)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (column < 0 || column >= TileColumns || row < 0 || row >= TileRows)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        var index = row * TileColumns + column;
        if (_cache.TryGetValue(index, out var cached))
        {
            return cached;
        }

        if (_region is not null)
        {
            if (_pendingRegionTiles.TryAdd(index, 0))
            {
                _ = LoadRegionTileAsync(column, row, index);
            }

            return null;
        }

        var bitmap = ReadTile(column, row);
        CacheTile(index, bitmap);
        return bitmap;
    }

    private void CacheTile(int index, WriteableBitmap bitmap)
    {
        if (_cache.Count >= MaxCachedTiles && _cacheOrder.Count > 0)
        {
            var evict = _cacheOrder.Dequeue();
            if (_cache.Remove(evict, out var old))
            {
                old.Dispose();
            }
        }

        _cache[index] = bitmap;
        _cacheOrder.Enqueue(index);
    }

    private async Task LoadRegionTileAsync(int column, int row, int index)
    {
        try
        {
            await _regionGate.WaitAsync();
            try
            {
                if (_disposed || _region is null)
                {
                    return;
                }

                var x = column * TileSize;
                var y = row * TileSize;
                var width = Math.Min(TileSize, PixelWidth - x);
                var height = Math.Min(TileSize, PixelHeight - y);
                var bytes = await Task.Run(() => _region.ReadTile(x, y, width, height));
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    var bitmap = CreateBitmap(bytes, width, height);
                    CacheTile(index, bitmap);
                    _pendingRegionTiles.TryRemove(index, out _);
                    TileAvailable?.Invoke(this, EventArgs.Empty);
                });
            }
            finally
            {
                _regionGate.Release();
            }
        }
        catch
        {
            _pendingRegionTiles.TryRemove(index, out _);
        }
    }

    private static WriteableBitmap CreateBitmap(byte[] bytes, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = bitmap.Lock();
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(bytes, y * rowBytes, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), rowBytes);
        }

        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
        _cacheOrder.Clear();
        _region?.Dispose();
        _stream?.Dispose();
        if (_path is not null)
        {
            TryDelete(_path);
        }
    }

    internal byte[] ReadTileBytesForTest(int column, int row)
    {
        var buffer = new byte[TileSize * TileSize * 4];
        var offset = HeaderSize + ((long)row * TileColumns + column) * _tileBytes;
        var stream = _stream ?? throw new InvalidOperationException("This tile source has no file stream.");
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private WriteableBitmap ReadTile(int column, int row)
    {
        var width = Math.Min(TileSize, PixelWidth - column * TileSize);
        var height = Math.Min(TileSize, PixelHeight - row * TileSize);
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        var rowBytes = width * 4;
        var tileBuffer = new byte[TileSize * TileSize * 4];
        var offset = HeaderSize + ((long)row * TileColumns + column) * _tileBytes;
        var stream = _stream ?? throw new InvalidOperationException("This tile source has no file stream.");
        stream.Position = offset;
        stream.ReadExactly(tileBuffer);

        using var framebuffer = bitmap.Lock();
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(
                tileBuffer,
                y * TileSize * 4,
                IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                rowBytes);
        }

        return bitmap;
    }

    private static void WriteTileFile(
        string path,
        DecodedBitmap full,
        int tileColumns,
        int tileRows,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1 << 20,
            FileOptions.SequentialScan);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(1);
            writer.Write(full.Width);
            writer.Write(full.Height);
            writer.Write(TileSize);
            writer.Write(tileColumns);
            writer.Write(tileRows);
            writer.Write(0L);
            writer.Flush();
        }

        stream.Position = HeaderSize;
        var tile = new byte[TileSize * TileSize * 4];
        var sourceStride = full.Stride;
        for (var row = 0; row < tileRows; row++)
        {
            for (var column = 0; column < tileColumns; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(tile);
                var width = Math.Min(TileSize, full.Width - column * TileSize);
                var height = Math.Min(TileSize, full.Height - row * TileSize);
                for (var y = 0; y < height; y++)
                {
                    var sourceOffset = (row * TileSize + y) * sourceStride + column * TileSize * 4;
                    Buffer.BlockCopy(full.Pixels, sourceOffset, tile, y * TileSize * 4, width * 4);
                }

                stream.Write(tile, 0, tile.Length);
            }
        }

        stream.Flush(flushToDisk: false);
    }

    private static unsafe void WriteTileFileRaw(
        string path,
        DecodedImageBuffer raw,
        int destinationWidth,
        int destinationHeight,
        int tileColumns,
        int tileRows,
        EditTransform edit,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1 << 20,
            FileOptions.SequentialScan);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(1);
            writer.Write(destinationWidth);
            writer.Write(destinationHeight);
            writer.Write(TileSize);
            writer.Write(tileColumns);
            writer.Write(tileRows);
            writer.Write(0L);
            writer.Flush();
        }

        stream.Position = HeaderSize;
        var tile = new byte[TileSize * TileSize * 4];
        var source = (uint*)raw.Pixels;
        var orientation = (int)raw.Orientation.Normalize();
        var fastPath = orientation == 1 && edit.IsIdentity;
        fixed (byte* tilePointer = tile)
        {
            var destination = (uint*)tilePointer;
            for (var row = 0; row < tileRows; row++)
            {
                for (var column = 0; column < tileColumns; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Array.Clear(tile);
                    var width = Math.Min(TileSize, destinationWidth - column * TileSize);
                    var height = Math.Min(TileSize, destinationHeight - row * TileSize);

                    if (fastPath)
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var sourceOffset = ((row * TileSize + y) * raw.Width) + column * TileSize;
                            Buffer.MemoryCopy(
                                source + sourceOffset,
                                destination + y * TileSize,
                                TileSize * 4,
                                width * 4);
                        }
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var (sourceX, sourceY) = MapToRaw(
                                    column * TileSize + x,
                                    row * TileSize + y,
                                    raw,
                                    orientation,
                                    edit);
                                destination[y * TileSize + x] = source[sourceY * raw.Width + sourceX];
                            }
                        }
                    }

                    stream.Write(tile, 0, tile.Length);
                }
            }
        }

        stream.Flush(flushToDisk: false);
    }

    private static (int X, int Y) MapToRaw(
        int x,
        int y,
        DecodedImageBuffer raw,
        int orientation,
        EditTransform edit)
    {
        var orientedWidth = raw.SourceWidth;
        var orientedHeight = raw.SourceHeight;
        int workX;
        int workY;
        switch (edit.QuarterTurns & 3)
        {
            case 1:
                workX = y;
                workY = orientedHeight - 1 - x;
                break;
            case 2:
                workX = orientedWidth - 1 - x;
                workY = orientedHeight - 1 - y;
                break;
            case 3:
                workX = orientedWidth - 1 - y;
                workY = x;
                break;
            default:
                workX = x;
                workY = y;
                break;
        }

        if (edit.MirrorHorizontal)
        {
            workX = orientedWidth - 1 - workX;
        }

        return MapOrientation(workX, workY, raw.Width, raw.Height, orientation);
    }

    private static (int X, int Y) MapOrientation(
        int x,
        int y,
        int width,
        int height,
        int orientation) =>
        orientation switch
        {
            2 => (width - 1 - x, y),
            3 => (width - 1 - x, height - 1 - y),
            4 => (x, height - 1 - y),
            5 => (y, x),
            6 => (y, height - 1 - x),
            7 => (width - 1 - y, height - 1 - x),
            8 => (width - 1 - y, x),
            _ => (x, y)
        };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A leftover temp file is harmless; the OS cleans the temp directory.
        }
    }
}

