using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

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

    private const int MaxCachedTiles = 32;

    /// <summary>Ceiling for the scanline band a streamed tile write buffers at once.</summary>
    private const long MaxStreamBandBytes = 64L * 1024 * 1024;

    private readonly string? _path;
    private readonly FileStream? _stream;
    private readonly TiffRegionReader? _region;
    private readonly SemaphoreSlim _regionGate = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _pendingRegionTiles = [];
    private readonly SemaphoreSlim _fileTileGate = new(1, 1);

    /// <summary>Tile indices whose file read is in flight. Touched on the UI thread only.</summary>
    private readonly HashSet<int> _pendingFileTiles = [];
    private readonly Dictionary<int, WriteableBitmap> _cache = [];
    private readonly Queue<int> _cacheOrder = [];
    private readonly CancellationTokenSource _regionPrepCts = new();
    private readonly long _tileBytes;
    private bool _disposed;

    public event EventHandler? TileAvailable;

    public event EventHandler? TileLoadingChanged;

    public event EventHandler? RegionProgressChanged;

    public int PendingTileCount => _pendingRegionTiles.Count;

    public double RegionProgress { get; private set; }

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
        : TileFileWriter.HeaderSize + (long)TileColumns * TileRows * _tileBytes;

    public static TiledImageSource OpenRegion(string path, int pixelWidth, int pixelHeight)
    {
        var region = TiffRegionReader.Open(path);
        var source = new TiledImageSource(region, pixelWidth, pixelHeight);
        source.StartRegionPreparation();
        return source;
    }

    private void StartRegionPreparation()
    {
        if (_region is null || !_region.RequiresStreamingCache)
        {
            return;
        }

        _region.CacheProgressChanged += OnRegionCacheProgress;
        _ = Task.Run(
            () =>
            {
                try
                {
                    _region.Prepare(_regionPrepCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Leaving the region cancels the prep.
                }
                catch
                {
                    // The tile request will surface the same failure.
                }
            });
    }

    private void OnRegionCacheProgress(double progress)
    {
        RegionProgress = progress;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_disposed)
                {
                    RegionProgressChanged?.Invoke(this, EventArgs.Empty);
                }
            },
            DispatcherPriority.Background);
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
        var expectedSize = TileFileWriter.HeaderSize + (long)tileColumns * tileRows * tileBytes;

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
            TileFileWriter.WriteTileFile(path, full, tileColumns, tileRows, cancellationToken);
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
        var expectedSize = TileFileWriter.HeaderSize + (long)tileColumns * tileRows * tileBytes;

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
            TileFileWriter.WriteTileFileRaw(
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
    /// Writes the tile file straight from the encoder's scanlines, so a JPEG never needs
    /// the 400 MB full-resolution raster that <see cref="Create(DecodedImageBuffer, CancellationToken)"/>
    /// requires. Returns null when the file cannot be streamed this way - a non-JPEG, an
    /// EXIF rotation (the tile writer would need the transposed source), a codec without
    /// scanline support, or a band too wide to buffer - and the caller falls back to the
    /// raster path.
    /// </summary>
    public static TiledImageSource? CreateStreamed(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsStreamableJpeg(path))
        {
            return null;
        }

        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var codec = SKCodec.Create(input);
        if (codec is null)
        {
            return null;
        }

        if ((int)codec.EncodedOrigin is >= 2 and <= 8)
        {
            return null;
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var rowBytes = checked(width * 4);
        var bandBytes = (long)rowBytes * TileSize;
        if (bandBytes > MaxStreamBandBytes)
        {
            return null;
        }

        var info = new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());
        if (codec.StartScanlineDecode(info) != SKCodecResult.Success)
        {
            return null;
        }

        var tileColumns = (width + TileSize - 1) / TileSize;
        var tileRows = (height + TileSize - 1) / TileSize;
        var expectedSize = TileFileWriter.HeaderSize + (long)tileColumns * tileRows * (long)TileSize * TileSize * 4;
        var directory = Path.Combine(Path.GetTempPath(), "AirGlance", "tiles");
        Directory.CreateDirectory(directory);
        var tilePath = Path.Combine(directory, $"{Guid.NewGuid():N}.tiles");

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
            TileFileWriter.WriteStreamedTileFile(
                tilePath,
                codec,
                width,
                height,
                tileColumns,
                tileRows,
                bandBytes,
                cancellationToken);
            var stream = new FileStream(
                tilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 20,
                FileOptions.RandomAccess);
            return new TiledImageSource(tilePath, stream, width, height);
        }
        catch
        {
            TryDelete(tilePath);
            throw;
        }
    }

    public static Task<TiledImageSource?> CreateStreamedAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateStreamed(path, cancellationToken), cancellationToken);

    private static bool IsStreamableJpeg(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jfif", StringComparison.OrdinalIgnoreCase);
    }

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
                RaiseTileLoadingChanged();
                _ = LoadRegionTileAsync(column, row, index);
            }

            return null;
        }

        // Reading the tile file is a blocking 1 MB read. Doing it here meant a cache miss
        // inside Render stalled the frame, up to 32 times per pan, so the read is queued
        // instead and the caller draws the preview underneath until the tile arrives.
        if (_pendingFileTiles.Add(index))
        {
            RaiseTileLoadingChanged();
            _ = LoadFileTileAsync(column, row, index);
        }

        return null;
    }

    private async Task LoadFileTileAsync(int column, int row, int index)
    {
        WriteableBitmap? bitmap = null;
        try
        {
            await _fileTileGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_disposed)
                {
                    bitmap = await Task.Run(() => ReadTile(column, row)).ConfigureAwait(false);
                }
            }
            finally
            {
                _fileTileGate.Release();
            }
        }
        catch
        {
            // A failed read leaves the preview visible; the next pan retries.
            bitmap = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pendingFileTiles.Remove(index);
            if (bitmap is null || _disposed)
            {
                bitmap?.Dispose();
                return;
            }

            CacheTile(index, bitmap);
            TileLoadingChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void RaiseTileLoadingChanged() =>
        Dispatcher.UIThread.Post(
            () => TileLoadingChanged?.Invoke(this, EventArgs.Empty),
            DispatcherPriority.Background);

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
                    RaiseTileLoadingChanged();
                });
            }
            finally
            {
                _regionGate.Release();
            }
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pendingRegionTiles.TryRemove(index, out _);
                RaiseTileLoadingChanged();
            });
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
        _regionPrepCts.Cancel();
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
        _cacheOrder.Clear();
        _pendingFileTiles.Clear();
        _fileTileGate.Dispose();
        _region?.Dispose();
        _regionPrepCts.Dispose();
        _stream?.Dispose();
        if (_path is not null)
        {
            TryDelete(_path);
        }
    }

    internal byte[] ReadTileBytesForTest(int column, int row)
    {
        var buffer = new byte[TileSize * TileSize * 4];
        var offset = TileFileWriter.HeaderSize + ((long)row * TileColumns + column) * _tileBytes;
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
        var offset = TileFileWriter.HeaderSize + ((long)row * TileColumns + column) * _tileBytes;
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

