using System.Runtime.InteropServices;
using AirGlance.Core.Imaging;
using SkiaSharp;

namespace AirGlance.App.Imaging;

/// <summary>
/// The on-disk tile format: a 64-byte header followed by fixed-size 512 x 512 BGRA tiles in
/// row-major order, edges padded. The three writers live here so the format is described in
/// one place; <see cref="TiledImageSource"/> owns the read side, the LRU and the loading.
/// </summary>
internal static class TileFileWriter
{
    public const int TileSize = 512;

    public const int HeaderSize = 64;

    private const uint Magic = 0x4C544741; // AGTL

    /// <summary>
    /// Streams the encoder's scanlines into the tile file one band (one tile row) at a
    /// time. Peak memory is the band buffer plus one tile, so a 100 MP JPEG costs about
    /// 21 MB instead of the 400 MB raster the raster writer needs.
    /// </summary>
    public static unsafe void WriteStreamedTileFile(
        string path,
        SKCodec codec,
        int width,
        int height,
        int tileColumns,
        int tileRows,
        long bandBytes,
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
            writer.Write(width);
            writer.Write(height);
            writer.Write(TileSize);
            writer.Write(tileColumns);
            writer.Write(tileRows);
            writer.Write(0L);
            writer.Flush();
        }

        stream.Position = HeaderSize;
        var rowBytes = checked(width * 4);
        var band = GC.AllocateUninitializedArray<byte>((int)bandBytes);
        var tile = new byte[TileSize * TileSize * 4];
        fixed (byte* bandPointer = band)
        fixed (byte* tilePointer = tile)
        {
            var source = (uint*)bandPointer;
            var destination = (uint*)tilePointer;
            var bandRow = 0;
            while (bandRow < height)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = Math.Min(TileSize, height - bandRow);
                var decoded = 0;
                while (decoded < rows)
                {
                    var lines = codec.GetScanlines(
                        (nint)(bandPointer + (long)decoded * rowBytes),
                        rows - decoded,
                        rowBytes);
                    if (lines <= 0)
                    {
                        throw new ImageLoadException(
                            ImageLoadFailure.Corrupt,
                            "The image could not be decoded row by row.",
                            path,
                            Path.GetExtension(path));
                    }

                    decoded += lines;
                }

                for (var column = 0; column < tileColumns; column++)
                {
                    Array.Clear(tile);
                    var tileWidth = Math.Min(TileSize, width - column * TileSize);
                    for (var y = 0; y < rows; y++)
                    {
                        Buffer.MemoryCopy(
                            source + ((long)y * width) + column * TileSize,
                            destination + y * TileSize,
                            TileSize * 4,
                            tileWidth * 4);
                    }

                    stream.Write(tile, 0, tile.Length);
                }

                bandRow += rows;
            }
        }

        stream.Flush(flushToDisk: false);
    }

    public static void WriteTileFile(
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

    public static unsafe void WriteTileFileRaw(
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
}
