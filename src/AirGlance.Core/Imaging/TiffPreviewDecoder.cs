using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BitMiracle.LibTiff.Classic;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Managed metadata and preview decoding for TIFFs too large for a full raster.
/// Tiles or strips are decoded one at a time and sampled into the target bitmap,
/// so a 40K TIFF never needs a full-size managed buffer. The preview keeps the
/// stored orientation; the region tile path and the tile cache handle it as well.
/// </summary>
public static class TiffPreviewDecoder
{
    public const int MaximumPreviewEdge = 8192;

    // One tile/strip raster larger than this would defeat bounded-memory preview.
    private const long MaximumRasterBytes = 64L * 1024 * 1024;

    private const long ParallelPixelThreshold = 2_000_000;
    private const long ParallelWorkerMemoryBudget = 256L * 1024 * 1024;
    private const int MaximumParallelWorkers = 8;
    private const int MaximumPyramidDepth = 8;
    private const double PyramidUpscaleTolerance = 0.95;

    public static ImageMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var file = new FileInfo(path);
            using var image = OpenRaw(path);
            var width = TagInt(image, TiffTag.IMAGEWIDTH);
            var height = TagInt(image, TiffTag.IMAGELENGTH);
            if (width <= 0 || height <= 0)
            {
                throw Corrupt(path, "The TIFF has no usable image dimensions.");
            }

            var bitDepth = TagInt(image, TiffTag.BITSPERSAMPLE);
            var icc = TagBytes(image, TiffTag.ICCPROFILE);
            return new ImageMetadata
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = file.Length,
                LastWriteTime = file.LastWriteTimeUtc,
                RawWidth = width,
                RawHeight = height,
                DisplayWidth = width,
                DisplayHeight = height,
                Orientation = ImageOrientation.TopLeft,
                FormatName = "TIFF",
                FrameCount = 1,
                BitDepth = bitDepth is > 0 and <= 32 ? bitDepth : 8,
                IccDescription = IccProfile.ReadDescription(icc)
            };
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.Corrupt,
                "The TIFF metadata could not be read.",
                path,
                Path.GetExtension(path),
                exception);
        }
    }

    public static DecodedBitmap DecodePreview(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var image = OpenRaw(path);
            var width = TagInt(image, TiffTag.IMAGEWIDTH);
            var height = TagInt(image, TiffTag.IMAGELENGTH);
            if (width <= 0 || height <= 0)
            {
                throw Corrupt(path, "The TIFF has no usable image dimensions.");
            }

            var (targetWidth, targetHeight, scale) = ComputeTarget(width, height, maxWidth, maxHeight);
            var pixels = new byte[checked(targetWidth * targetHeight * 4)];

            // Prefer a reduced pyramid level when one is large enough for the
            // requested preview. The selected level is small enough to decode on
            // the calling handle, so no worker has to re-open a sub-directory.
            var decodedReducedLevel = false;
            var level = FindReducedLevel(image, path, width, height, targetWidth, targetHeight);
            if (level is { } reduced && TrySwitchToLevel(image, reduced))
            {
                var decodeWidth = TagInt(image, TiffTag.IMAGEWIDTH);
                var decodeHeight = TagInt(image, TiffTag.IMAGELENGTH);
                if (decodeWidth > 0 && decodeHeight > 0)
                {
                    var sampleScale = Math.Min(
                        1.0,
                        Math.Min(targetWidth / (double)decodeWidth, targetHeight / (double)decodeHeight));
                    DecodeCurrentDirectory(
                        image,
                        path,
                        decodeWidth,
                        decodeHeight,
                        targetWidth,
                        targetHeight,
                        sampleScale,
                        pixels,
                        allowParallel: true,
                        reduced.Directory,
                        reduced.SubIfdOffset,
                        cancellationToken);
                    decodedReducedLevel = true;
                }
            }

            if (!decodedReducedLevel)
            {
                image.SetDirectory(0);
                ForceStoredOrientation(image);
                DecodeCurrentDirectory(
                    image,
                    path,
                    width,
                    height,
                    targetWidth,
                    targetHeight,
                    scale,
                    pixels,
                    allowParallel: true,
                    directory: 0,
                    subIfdOffset: 0,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new DecodedBitmap(
                targetWidth,
                targetHeight,
                pixels,
                width,
                height,
                scale,
                isProxy: true)
            {
                HasAlpha = true
            };
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException exception)
        {
            foreach (var inner in exception.Flatten().InnerExceptions)
            {
                if (inner is ImageLoadException imageLoad)
                {
                    throw imageLoad;
                }

                if (inner is OperationCanceledException canceled)
                {
                    throw canceled;
                }
            }

            throw;
        }
        catch (Exception exception)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF preview could not be decoded.",
                path,
                Path.GetExtension(path),
                exception);
        }
    }

    private static void DecodeCurrentDirectory(
        Tiff image,
        string path,
        int decodeWidth,
        int decodeHeight,
        int targetWidth,
        int targetHeight,
        double sampleScale,
        byte[] pixels,
        bool allowParallel,
        short directory,
        long subIfdOffset,
        CancellationToken cancellationToken)
    {
        var format = GetRawFormat(image);
        if (!image.IsTiled() && format.Layout is RawLayout.RgbPlanar8 or RawLayout.RgbPlanar16 or
            RawLayout.CmykPlanar8 or RawLayout.RgbaPlanar8 or RawLayout.RgbaPlanar16)
        {
            DecodeStrippedPlanar(
                image,
                path,
                decodeWidth,
                decodeHeight,
                targetWidth,
                targetHeight,
                sampleScale,
                format,
                pixels,
                cancellationToken);
            return;
        }

        if (!image.IsTiled() && format.Layout == RawLayout.YCbCrSub8)
        {
            DecodeStrippedYCbCrSubsampled(
                image,
                path,
                decodeWidth,
                decodeHeight,
                targetWidth,
                targetHeight,
                sampleScale,
                format,
                pixels,
                cancellationToken);
            return;
        }

        if (image.IsTiled())
        {
            if (allowParallel &&
                TryDecodeTiledParallel(
                    image,
                    path,
                    decodeWidth,
                    decodeHeight,
                    targetWidth,
                    targetHeight,
                    sampleScale,
                    format,
                    pixels,
                    directory,
                    subIfdOffset,
                    cancellationToken))
            {
                return;
            }

            if (format.Layout == RawLayout.None)
            {
                DecodeTiledRgba(
                    image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, pixels, cancellationToken);
            }
            else
            {
                DecodeTiledRaw(
                    image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, format, pixels, cancellationToken);
            }

            return;
        }

        if (allowParallel &&
            TryDecodeStrippedParallel(
                image,
                path,
                decodeWidth,
                decodeHeight,
                targetWidth,
                targetHeight,
                sampleScale,
                format,
                pixels,
                directory,
                subIfdOffset,
                cancellationToken))
        {
            return;
        }

        if (format.Layout == RawLayout.None)
        {
            DecodeStrippedRgba(
                image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, pixels, cancellationToken);
        }
        else
        {
            DecodeStrippedRaw(
                image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, format, pixels, cancellationToken);
        }
    }

    private static ReducedLevel? FindReducedLevel(
        Tiff image,
        string path,
        int fullWidth,
        int fullHeight,
        int targetWidth,
        int targetHeight)
    {
        var fullArea = (long)fullWidth * fullHeight;
        var fullAspect = fullWidth / (double)fullHeight;
        ReducedLevel? best = null;
        var bestArea = long.MaxValue;

        void Consider(int width, int height, short directory, long subIfdOffset)
        {
            if (width <= 0 || height <= 0 ||
                width < targetWidth * PyramidUpscaleTolerance ||
                height < targetHeight * PyramidUpscaleTolerance)
            {
                return;
            }

            var area = (long)width * height;
            if (area >= fullArea || area >= bestArea)
            {
                return;
            }

            var aspect = width / (double)height;
            if (Math.Abs(aspect - fullAspect) > fullAspect * 0.01)
            {
                return;
            }

            best = new ReducedLevel(width, height, directory, subIfdOffset);
            bestArea = area;
        }

        var visited = new HashSet<long>();
        var directoryOffsets = ReadTopLevelIfdOffsets(path);
        var directoryCount = image.NumberOfDirectories();
        for (short directory = 0; directory < directoryCount; directory++)
        {
            if (!image.SetDirectory(directory))
            {
                continue;
            }

            var currentIfdOffset = directory < directoryOffsets.Count ? directoryOffsets[directory] : 0;
            VisitLevel(image, directory, 0, currentIfdOffset, 0);
        }

        image.SetDirectory(0);
        return best;

        void VisitLevel(Tiff current, short directory, long subIfdOffset, long currentIfdOffset, int depth)
        {
            var subfileField = current.GetField(TiffTag.SUBFILETYPE);
            var subfileType = subfileField is { Length: > 0 } ? subfileField[0].ToInt() : 0;
            var isPage = (subfileType & 2) != 0;
            var isMask = (subfileType & 4) != 0;
            if (isPage || isMask)
            {
                return;
            }

            Consider(
                TagInt(current, TiffTag.IMAGEWIDTH),
                TagInt(current, TiffTag.IMAGELENGTH),
                directory,
                subIfdOffset);

            if (depth >= MaximumPyramidDepth)
            {
                return;
            }

            var offsets = ReadSubIfdOffsets(current);
            if (offsets.Length == 0 && currentIfdOffset > 0)
            {
                offsets = ReadSubIfdOffsetsFromFile(path, currentIfdOffset);
            }

            foreach (var offset in offsets)
            {
                if (offset <= 0 || !visited.Add(offset))
                {
                    continue;
                }

                if (!current.SetSubDirectory(offset))
                {
                    continue;
                }

                VisitLevel(current, 0, offset, offset, depth + 1);

                // Return to this level before visiting the next child.
                if (subIfdOffset > 0)
                {
                    current.SetSubDirectory(subIfdOffset);
                }
                else
                {
                    current.SetDirectory(directory);
                }
            }
        }
    }

    private static bool TrySwitchToLevel(Tiff image, ReducedLevel level)
    {
        var switched = level.SubIfdOffset > 0
            ? image.SetSubDirectory(level.SubIfdOffset)
            : image.SetDirectory(level.Directory);
        if (!switched)
        {
            return false;
        }

        ForceStoredOrientation(image);
        return true;
    }

    private static void ForceStoredOrientation(Tiff image)
    {
        var orientation = TagInt(image, TiffTag.ORIENTATION);
        if (orientation is > 1 and <= 8)
        {
            image.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        }
    }

    private static long[] ReadSubIfdOffsets(Tiff image)
    {
        var field = image.GetField(TiffTag.SUBIFD);
        if (field is null)
        {
            return [];
        }

        foreach (var value in field)
        {
            switch (value.Value)
            {
                case long[] offsets:
                    return offsets;
                case int[] ints:
                {
                    var offsets = new long[ints.Length];
                    for (var index = 0; index < ints.Length; index++)
                    {
                        offsets[index] = ints[index];
                    }

                    return offsets;
                }

                case uint[] uints:
                {
                    var offsets = new long[uints.Length];
                    for (var index = 0; index < uints.Length; index++)
                    {
                        offsets[index] = uints[index];
                    }

                    return offsets;
                }
            }
        }

        return [];
    }

    private readonly record struct IfdLayout(bool Little, bool BigTiff, long FirstIfdOffset)
    {
        public int CountSize => BigTiff ? 8 : 2;

        public int EntrySize => BigTiff ? 20 : 12;

        public int OffsetSize => BigTiff ? 8 : 4;
    }

    private static List<long> ReadTopLevelIfdOffsets(string path)
    {
        var offsets = new List<long>();
        try
        {
            using var stream = OpenIfdStream(path);
            if (!TryReadIfdLayout(stream, out var layout))
            {
                return offsets;
            }

            var offset = layout.FirstIfdOffset;
            while (offset > 0 && offsets.Count < 4096)
            {
                offsets.Add(offset);
                stream.Position = offset;
                var count = layout.BigTiff
                    ? ReadUInt64(stream, layout.Little)
                    : ReadUInt16(stream, layout.Little);
                if (count > 1_000_000)
                {
                    break;
                }

                var nextPosition = offset + layout.CountSize + (long)count * layout.EntrySize;
                if (nextPosition < 0 || nextPosition + layout.OffsetSize > stream.Length)
                {
                    break;
                }

                stream.Position = nextPosition;
                offset = layout.BigTiff
                    ? (long)ReadUInt64(stream, layout.Little)
                    : ReadUInt32(stream, layout.Little);
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ArgumentException)
        {
            // A broken directory chain just means no raw IFD fallback.
        }

        return offsets;
    }

    private static long[] ReadSubIfdOffsetsFromFile(string path, long ifdOffset)
    {
        try
        {
            using var stream = OpenIfdStream(path);
            return TryReadIfdLayout(stream, out var layout)
                ? ReadSubIfdOffsetsAt(stream, ifdOffset, layout)
                : [];
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ArgumentException)
        {
            return [];
        }
    }

    private static long[] ReadSubIfdOffsetsAt(Stream stream, long ifdOffset, in IfdLayout layout)
    {
        if (ifdOffset <= 0 || ifdOffset + layout.CountSize > stream.Length)
        {
            return [];
        }

        stream.Position = ifdOffset;
        var count = layout.BigTiff
            ? ReadUInt64(stream, layout.Little)
            : ReadUInt16(stream, layout.Little);
        if (count is 0 or > 4096)
        {
            return [];
        }

        var entryBase = ifdOffset + layout.CountSize;
        for (ulong index = 0; index < count; index++)
        {
            var position = entryBase + (long)index * layout.EntrySize;
            if (position + layout.EntrySize > stream.Length)
            {
                return [];
            }

            stream.Position = position;
            var tag = ReadUInt16(stream, layout.Little);
            var type = ReadUInt16(stream, layout.Little);
            var itemCount = layout.BigTiff
                ? ReadUInt64(stream, layout.Little)
                : ReadUInt32(stream, layout.Little);
            var valuePosition = stream.Position;
            if (tag != 330)
            {
                continue;
            }

            if (type is not (4 or 13 or 18) || itemCount is 0 or > 4096)
            {
                return [];
            }

            var offsetWidth = type == 18 || (layout.BigTiff && type == 13) ? 8 : 4;
            var valueSize = layout.BigTiff ? 8 : 4;
            var inline = itemCount * (ulong)offsetWidth <= (ulong)valueSize;
            if (inline)
            {
                stream.Position = valuePosition;
            }
            else
            {
                stream.Position = valuePosition;
                var dataOffset = offsetWidth == 8
                    ? (long)ReadUInt64(stream, layout.Little)
                    : ReadUInt32(stream, layout.Little);
                if (dataOffset <= 0 || dataOffset > stream.Length)
                {
                    return [];
                }

                stream.Position = dataOffset;
            }

            var result = new long[(int)itemCount];
            for (var item = 0; item < result.Length; item++)
            {
                result[item] = offsetWidth == 8
                    ? (long)ReadUInt64(stream, layout.Little)
                    : ReadUInt32(stream, layout.Little);
            }

            return result;
        }

        return [];
    }

    private static FileStream OpenIfdStream(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.RandomAccess);

    private static bool TryReadIfdLayout(Stream stream, out IfdLayout layout)
    {
        layout = default;
        if (stream.Length < 8)
        {
            return false;
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[16];
        var headerLength = (int)Math.Min(header.Length, stream.Length);
        ReadExactly(stream, header[..headerLength]);
        var little = header[0] == (byte)'I' && header[1] == (byte)'I';
        var big = header[0] == (byte)'M' && header[1] == (byte)'M';
        if (!little && !big)
        {
            return false;
        }

        var magic = little
            ? BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2, 2));
        if (magic == 42)
        {
            var first = little
                ? BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
            layout = new IfdLayout(little, false, first);
            return true;
        }

        if (magic == 43 && headerLength >= 16)
        {
            var first = little
                ? BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(8, 8))
                : BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
            layout = new IfdLayout(little, true, (long)first);
            return true;
        }

        return false;
    }

    private static ushort ReadUInt16(Stream stream, bool little)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactly(stream, buffer);
        return little
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    private static uint ReadUInt32(Stream stream, bool little)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(stream, buffer);
        return little
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static ulong ReadUInt64(Stream stream, bool little)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactly(stream, buffer);
        return little
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count <= 0)
            {
                throw new EndOfStreamException();
            }

            read += count;
        }
    }

    private readonly record struct ReducedLevel(
        int Width,
        int Height,
        short Directory,
        long SubIfdOffset);

    private static bool TryDecodeTiledParallel(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        RawFormat format,
        byte[] pixels,
        short directory,
        long subIfdOffset,
        CancellationToken cancellationToken)
    {
        if ((long)width * height < ParallelPixelThreshold)
        {
            return false;
        }

        var tileWidth = TagInt(image, TiffTag.TILEWIDTH);
        var tileHeight = TagInt(image, TiffTag.TILELENGTH);
        if (tileWidth <= 0 || tileHeight <= 0)
        {
            return false;
        }

        var perWorkerBytes = format.Layout == RawLayout.None
            ? (long)tileWidth * tileHeight * sizeof(int)
            : image.TileSize();
        if (perWorkerBytes <= 0 || perWorkerBytes > MaximumRasterBytes)
        {
            return false;
        }

        var workers = GetWorkerCount(perWorkerBytes);
        if (workers <= 1)
        {
            return false;
        }

        var step = SampleStep(scale);
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = workers
        };
        Parallel.For(
            0,
            workers,
            options,
            worker => DecodeTiledBand(
                path,
                worker,
                workers,
                width,
                height,
                targetWidth,
                targetHeight,
                step,
                format,
                tileWidth,
                tileHeight,
                pixels,
                directory,
                subIfdOffset,
                cancellationToken));
        return true;
    }

    private static void DecodeTiledBand(
        string path,
        int worker,
        int workerCount,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        int step,
        RawFormat format,
        int tileWidth,
        int tileHeight,
        byte[] pixels,
        short directory,
        long subIfdOffset,
        CancellationToken cancellationToken)
    {
        var (bandStart, bandEnd) = Band(worker, workerCount, height);
        if (bandStart >= bandEnd)
        {
            return;
        }

        using var image = OpenAtLevel(path, directory, subIfdOffset);
        var tilesAcross = (width + tileWidth - 1) / tileWidth;
        var firstTileY = bandStart / tileHeight * tileHeight;
        if (format.Layout == RawLayout.None)
        {
            var raster = new int[tileWidth * tileHeight];
            for (var tileY = firstTileY; tileY < bandEnd; tileY += tileHeight)
            {
                for (var tileX = 0; tileX < width; tileX += tileWidth)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!image.ReadRGBATile(tileX, tileY, raster))
                    {
                        throw Corrupt(path, "The TIFF tile could not be decoded.");
                    }

                    var rowEnd = Math.Min(bandEnd, tileY + Math.Min(tileHeight, height - tileY));
                    var firstRow = RoundUp(Math.Max(bandStart, tileY), step);
                    var columnEnd = tileX + Math.Min(tileWidth, width - tileX);
                    var firstColumn = RoundUp(tileX, step);
                    for (var sourceY = firstRow; sourceY < rowEnd; sourceY += step)
                    {
                        var rasterRow = tileHeight - 1 - (sourceY - tileY);
                        var rowStart = rasterRow * tileWidth;
                        for (var sourceX = firstColumn; sourceX < columnEnd; sourceX += step)
                        {
                            WriteScaledValue(
                                pixels,
                                targetWidth,
                                targetHeight,
                                sourceX,
                                sourceY,
                                width,
                                height,
                                raster[rowStart + sourceX - tileX]);
                        }
                    }
                }
            }

            return;
        }

        var tileSize = image.TileSize();
        if (tileSize <= 0)
        {
            throw Corrupt(path, "The TIFF tile is not readable.");
        }

        var bytesPerPixel = RawPixelBytes(format.Layout);
        var raw = new byte[tileSize];
        for (var tileY = firstTileY; tileY < bandEnd; tileY += tileHeight)
        {
            for (var tileX = 0; tileX < width; tileX += tileWidth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tileIndex = (tileY / tileHeight) * tilesAcross + (tileX / tileWidth);
                var read = image.ReadEncodedTile(tileIndex, raw, 0, raw.Length);
                if (read <= 0)
                {
                    throw Corrupt(path, "The TIFF tile could not be decoded.");
                }

                var rowEnd = Math.Min(bandEnd, tileY + Math.Min(tileHeight, height - tileY));
                var firstRow = RoundUp(Math.Max(bandStart, tileY), step);
                var columnEnd = tileX + Math.Min(tileWidth, width - tileX);
                var firstColumn = RoundUp(tileX, step);
                for (var sourceY = firstRow; sourceY < rowEnd; sourceY += step)
                {
                    var rowOffset = (sourceY - tileY) * tileWidth;
                    for (var sourceX = firstColumn; sourceX < columnEnd; sourceX += step)
                    {
                        var offset = (rowOffset + sourceX - tileX) * bytesPerPixel;
                        if (offset + bytesPerPixel > read)
                        {
                            continue;
                        }

                        WriteScaled(
                            pixels,
                            targetWidth,
                            targetHeight,
                            sourceX,
                            sourceY,
                            width,
                            height,
                            ReadRawPixel(raw, offset, format));
                    }
                }
            }
        }
    }

    private static bool TryDecodeStrippedParallel(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        RawFormat format,
        byte[] pixels,
        short directory,
        long subIfdOffset,
        CancellationToken cancellationToken)
    {
        if ((long)width * height < ParallelPixelThreshold)
        {
            return false;
        }

        var rowsPerStrip = TagLong(image, TiffTag.ROWSPERSTRIP);
        if (rowsPerStrip <= 0 || rowsPerStrip > height)
        {
            rowsPerStrip = height;
        }

        if (rowsPerStrip >= height)
        {
            // A single strip cannot be split without decoding it sequentially.
            return false;
        }

        var rowsPerStripValue = (int)rowsPerStrip;
        var step = SampleStep(scale);
        if (format.Layout != RawLayout.None)
        {
            var stripSize = image.StripSize();
            if (stripSize <= 0 || stripSize > MaximumRasterBytes)
            {
                return false;
            }

            var workers = GetWorkerCount(stripSize);
            if (workers <= 1)
            {
                return false;
            }

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = workers
            };
            Parallel.For(
                0,
                workers,
                options,
                worker => DecodeStrippedRawBand(
                    path,
                    worker,
                    workers,
                    width,
                    height,
                    targetWidth,
                    targetHeight,
                    step,
                    format,
                    rowsPerStripValue,
                    stripSize,
                    pixels,
                    directory,
                    subIfdOffset,
                    cancellationToken));
            return true;
        }

        var rasterBytes = (long)width * rowsPerStripValue * sizeof(int);
        if (rasterBytes > MaximumRasterBytes)
        {
            return false;
        }

        var rasterWorkers = GetWorkerCount(rasterBytes);
        if (rasterWorkers <= 1)
        {
            return false;
        }

        var rasterOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = rasterWorkers
        };
        Parallel.For(
            0,
            rasterWorkers,
            rasterOptions,
            worker => DecodeStrippedRgbaBand(
                path,
                worker,
                rasterWorkers,
                width,
                height,
                targetWidth,
                targetHeight,
                step,
                rowsPerStripValue,
                pixels,
                directory,
                subIfdOffset,
                cancellationToken));
        return true;
    }

    private static void DecodeStrippedRawBand(
        string path,
        int worker,
        int workerCount,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        int step,
        RawFormat format,
        int rowsPerStrip,
        int stripSize,
        byte[] pixels,
        short directory,
        long subIfdOffset,
        CancellationToken cancellationToken)
    {
        var (bandStart, bandEnd) = Band(worker, workerCount, height);
        if (bandStart >= bandEnd)
        {
            return;
        }

        using var image = OpenAtLevel(path, directory, subIfdOffset);
        var raw = new byte[stripSize];
        var bytesPerPixel = RawPixelBytes(format.Layout);
        var firstStrip = bandStart / rowsPerStrip;
        var lastStrip = (bandEnd - 1) / rowsPerStrip;
        for (var strip = firstStrip; strip <= lastStrip; strip++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stripRow = strip * rowsPerStrip;
            var actualRows = Math.Min(rowsPerStrip, height - stripRow);
            var read = image.ReadEncodedStrip(strip, raw, 0, raw.Length);
            if (read <= 0)
            {
                throw Corrupt(path, "The TIFF strip could not be decoded.");
            }

            var rowStart = Math.Max(bandStart, stripRow);
            var rowEnd = Math.Min(bandEnd, stripRow + actualRows);
            var firstRow = RoundUp(rowStart, step);
            for (var sourceY = firstRow; sourceY < rowEnd; sourceY += step)
            {
                var rowOffset = (sourceY - stripRow) * width;
                for (var sourceX = 0; sourceX < width; sourceX += step)
                {
                    var offset = (rowOffset + sourceX) * bytesPerPixel;
                    if (offset + bytesPerPixel > read)
                    {
                        continue;
                    }

                    WriteScaled(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        ReadRawPixel(raw, offset, format));
                }
            }
        }
    }

    private static void DecodeStrippedRgbaBand(
        string path,
        int worker,
        int workerCount,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        int step,
        int rowsPerStrip,
        byte[] pixels,
        short directory,
        long subIfdOffset,
        CancellationToken cancellationToken)
    {
        var (bandStart, bandEnd) = Band(worker, workerCount, height);
        if (bandStart >= bandEnd)
        {
            return;
        }

        using var image = OpenAtLevel(path, directory, subIfdOffset);
        var raster = new int[width * rowsPerStrip];
        var firstStrip = bandStart / rowsPerStrip;
        var lastStrip = (bandEnd - 1) / rowsPerStrip;
        for (var strip = firstStrip; strip <= lastStrip; strip++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stripRow = strip * rowsPerStrip;
            var actualRows = Math.Min(rowsPerStrip, height - stripRow);
            if (!image.ReadRGBAStrip(stripRow, raster))
            {
                throw Corrupt(path, "The TIFF strip could not be decoded.");
            }

            var rowStart = Math.Max(bandStart, stripRow);
            var rowEnd = Math.Min(bandEnd, stripRow + actualRows);
            var firstRow = RoundUp(rowStart, step);
            for (var sourceY = firstRow; sourceY < rowEnd; sourceY += step)
            {
                var rasterRow = actualRows - 1 - (sourceY - stripRow);
                var rowOffset = rasterRow * width;
                for (var sourceX = 0; sourceX < width; sourceX += step)
                {
                    WriteScaledValue(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        raster[rowOffset + sourceX]);
                }
            }
        }
    }

    private static (int Start, int End) Band(int index, int count, int total)
    {
        var start = (int)((long)total * index / count);
        var end = (int)((long)total * (index + 1) / count);
        return (start, end);
    }

    private static int GetWorkerCount(long perWorkerBytes)
    {
        var workers = Math.Min(Environment.ProcessorCount, MaximumParallelWorkers);
        if (perWorkerBytes > 0)
        {
            workers = Math.Min(workers, (int)Math.Max(1, ParallelWorkerMemoryBudget / perWorkerBytes));
        }

        return Math.Max(1, workers);
    }

    private static void DecodeTiledRgba(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var tileWidth = TagInt(image, TiffTag.TILEWIDTH);
        var tileHeight = TagInt(image, TiffTag.TILELENGTH);
        if (tileWidth <= 0 || tileHeight <= 0)
        {
            throw Corrupt(path, "The TIFF has no usable tile geometry.");
        }

        var rasterBytes = (long)tileWidth * tileHeight * sizeof(int);
        if (rasterBytes > MaximumRasterBytes)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF tile is too large for a managed preview.",
                path,
                Path.GetExtension(path));
        }

        var raster = new int[(int)(rasterBytes / sizeof(int))];
        var step = SampleStep(scale);
        for (var tileY = 0; tileY < height; tileY += tileHeight)
        {
            for (var tileX = 0; tileX < width; tileX += tileWidth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!image.ReadRGBATile(tileX, tileY, raster))
                {
                    throw Corrupt(path, "The TIFF tile could not be decoded.");
                }

                var rowCount = Math.Min(tileHeight, height - tileY);
                var columnCount = Math.Min(tileWidth, width - tileX);
                var firstRow = RoundUp(tileY, step);
                for (var sourceY = firstRow; sourceY < tileY + rowCount; sourceY += step)
                {
                    var rasterRow = tileHeight - 1 - (sourceY - tileY);
                    var rowStart = rasterRow * tileWidth;
                    var firstColumn = RoundUp(tileX, step);
                    for (var sourceX = firstColumn; sourceX < tileX + columnCount; sourceX += step)
                    {
                        WriteScaledValue(
                            pixels,
                            targetWidth,
                            targetHeight,
                            sourceX,
                            sourceY,
                            width,
                            height,
                            raster[rowStart + sourceX - tileX]);
                    }
                }
            }
        }
    }

    private static void DecodeStrippedRgba(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var rowsPerStrip = TagLong(image, TiffTag.ROWSPERSTRIP);
        if (rowsPerStrip <= 0 || rowsPerStrip > height)
        {
            rowsPerStrip = height;
        }

        var rasterLength = (long)width * rowsPerStrip;
        if (rasterLength > MaximumRasterBytes / sizeof(int))
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF stores its pixels in a strip that is too large for a managed preview.",
                path,
                Path.GetExtension(path));
        }

        var raster = new int[(int)rasterLength];
        var step = SampleStep(scale);
        for (var stripRow = 0; stripRow < height; stripRow += (int)rowsPerStrip)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actualRows = Math.Min((int)rowsPerStrip, height - stripRow);
            if (!image.ReadRGBAStrip(stripRow, raster))
            {
                throw Corrupt(path, "The TIFF strip could not be decoded.");
            }

            var firstRow = RoundUp(stripRow, step);
            for (var sourceY = firstRow; sourceY < stripRow + actualRows; sourceY += step)
            {
                var rasterRow = actualRows - 1 - (sourceY - stripRow);
                var rowStart = rasterRow * width;
                for (var sourceX = 0; sourceX < width; sourceX += step)
                {
                    WriteScaledValue(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        raster[rowStart + sourceX]);
                }
            }
        }
    }

    private static void DecodeTiledRaw(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        RawFormat format,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var tileWidth = TagInt(image, TiffTag.TILEWIDTH);
        var tileHeight = TagInt(image, TiffTag.TILELENGTH);
        if (tileWidth <= 0 || tileHeight <= 0)
        {
            throw Corrupt(path, "The TIFF has no usable tile geometry.");
        }

        var tileSize = image.TileSize();
        if (tileSize <= 0 || tileSize > MaximumRasterBytes)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF tile is too large for a managed preview.",
                path,
                Path.GetExtension(path));
        }

        var bytesPerPixel = RawPixelBytes(format.Layout);
        var raw = new byte[tileSize];
        var step = SampleStep(scale);
        var tilesAcross = (width + tileWidth - 1) / tileWidth;
        for (var tileY = 0; tileY < height; tileY += tileHeight)
        {
            for (var tileX = 0; tileX < width; tileX += tileWidth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tileIndex = (tileY / tileHeight) * tilesAcross + (tileX / tileWidth);
                var read = image.ReadEncodedTile(tileIndex, raw, 0, raw.Length);
                if (read <= 0)
                {
                    throw Corrupt(path, "The TIFF tile could not be decoded.");
                }

                var rowCount = Math.Min(tileHeight, height - tileY);
                var columnCount = Math.Min(tileWidth, width - tileX);
                var firstRow = RoundUp(tileY, step);
                for (var sourceY = firstRow; sourceY < tileY + rowCount; sourceY += step)
                {
                    var rowOffset = (sourceY - tileY) * tileWidth;
                    var firstColumn = RoundUp(tileX, step);
                    for (var sourceX = firstColumn; sourceX < tileX + columnCount; sourceX += step)
                    {
                        var offset = (rowOffset + sourceX - tileX) * bytesPerPixel;
                        if (offset + bytesPerPixel > read)
                        {
                            continue;
                        }

                        WriteScaled(
                            pixels,
                            targetWidth,
                            targetHeight,
                            sourceX,
                            sourceY,
                            width,
                            height,
                            ReadRawPixel(raw, offset, format));
                    }
                }
            }
        }
    }

    private static void DecodeStrippedRaw(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        RawFormat format,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var bytesPerPixel = RawPixelBytes(format.Layout);
        var row = new byte[checked(width * bytesPerPixel)];
        var step = SampleStep(scale);
        for (var sourceY = 0; sourceY < height; sourceY++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!image.ReadScanline(row, sourceY))
            {
                throw Corrupt(path, "The TIFF scanline could not be decoded.");
            }

            if (sourceY % step != 0)
            {
                continue;
            }

            for (var sourceX = 0; sourceX < width; sourceX += step)
            {
                var offset = sourceX * bytesPerPixel;
                WriteScaled(
                    pixels,
                    targetWidth,
                    targetHeight,
                    sourceX,
                    sourceY,
                    width,
                    height,
                    ReadRawPixel(row, offset, format));
            }
        }
    }

    private static long[] ReadIfdTagValues(string path, TiffTag tag)
    {
        try
        {
            using var stream = OpenIfdStream(path);
            if (!TryReadIfdLayout(stream, out var layout))
            {
                return [];
            }

            var ifdOffset = layout.FirstIfdOffset;
            if (ifdOffset <= 0 || ifdOffset + layout.CountSize > stream.Length)
            {
                return [];
            }

            stream.Position = ifdOffset;
            var entryCount = layout.BigTiff
                ? ReadUInt64(stream, layout.Little)
                : ReadUInt16(stream, layout.Little);
            if (entryCount is 0 or > 4096)
            {
                return [];
            }

            var entryBase = ifdOffset + layout.CountSize;
            for (ulong index = 0; index < entryCount; index++)
            {
                var position = entryBase + (long)index * layout.EntrySize;
                if (position + layout.EntrySize > stream.Length)
                {
                    return [];
                }

                stream.Position = position;
                var entryTag = ReadUInt16(stream, layout.Little);
                var type = ReadUInt16(stream, layout.Little);
                var count = layout.BigTiff
                    ? ReadUInt64(stream, layout.Little)
                    : ReadUInt32(stream, layout.Little);
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
                var valueSize = layout.BigTiff ? 8 : 4;
                if (total > valueSize)
                {
                    stream.Position = valuePosition;
                    var dataOffset = layout.BigTiff
                        ? (long)ReadUInt64(stream, layout.Little)
                        : ReadUInt32(stream, layout.Little);
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
                        3 => ReadUInt16(stream, layout.Little),
                        4 or 13 => layout.BigTiff && type == 13
                            ? (long)ReadUInt64(stream, layout.Little)
                            : ReadUInt32(stream, layout.Little),
                        _ => (long)ReadUInt64(stream, layout.Little)
                    };
                }

                return result;
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ArgumentException)
        {
            // Fall back to the LibTiff tag when the raw IFD is unreadable.
        }

        return [];
    }

    private static void DecodeStrippedPlanar(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        RawFormat format,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var rowsPerStrip = TagLong(image, TiffTag.ROWSPERSTRIP);
        if (rowsPerStrip <= 0 || rowsPerStrip > height)
        {
            rowsPerStrip = height;
        }

        var stripsPerPlane = (height + (int)rowsPerStrip - 1) / (int)rowsPerStrip;
        var wide = format.Layout is RawLayout.RgbPlanar16 or RawLayout.RgbaPlanar16;
        var bytesPerSample = wide ? 2 : 1;
        var planeSize = checked(width * (int)rowsPerStrip * bytesPerSample);
        if (planeSize > MaximumRasterBytes)
        {
            throw Corrupt(path, "The planar TIFF strip is too large.");
        }

        var planeCount = format.Layout is RawLayout.CmykPlanar8 or RawLayout.RgbaPlanar8 or RawLayout.RgbaPlanar16
            ? 4
            : 3;
        var planes = new byte[planeCount][];
        for (var plane = 0; plane < planeCount; plane++)
        {
            planes[plane] = new byte[planeSize];
        }

        var step = SampleStep(scale);
        for (var strip = 0; strip < stripsPerPlane; strip++)
        {
            var stripRow = strip * (int)rowsPerStrip;
            var rows = Math.Min((int)rowsPerStrip, height - stripRow);
            for (var plane = 0; plane < planeCount; plane++)
            {
                if (image.ReadEncodedStrip(plane * stripsPerPlane + strip, planes[plane], 0, planeSize) <= 0)
                {
                    throw Corrupt(path, "The planar TIFF strip could not be decoded.");
                }
            }

            for (var localRow = 0; localRow < rows; localRow++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceY = stripRow + localRow;
                if (sourceY % step != 0)
                {
                    continue;
                }

                var rowOffset = localRow * width * bytesPerSample;
                for (var sourceX = 0; sourceX < width; sourceX += step)
                {
                    var sampleOffset = rowOffset + sourceX * bytesPerSample;
                    byte red;
                    byte green;
                    byte blue;
                    byte alpha = 255;
                    if (wide)
                    {
                        red = (byte)(ReadUInt16Native(planes[0], sampleOffset) >> 8);
                        green = (byte)(ReadUInt16Native(planes[1], sampleOffset) >> 8);
                        blue = (byte)(ReadUInt16Native(planes[2], sampleOffset) >> 8);
                        if (format.Layout == RawLayout.RgbaPlanar16)
                        {
                            alpha = (byte)(ReadUInt16Native(planes[3], sampleOffset) >> 8);
                        }
                    }
                    else if (format.Layout == RawLayout.CmykPlanar8)
                    {
                        red = (byte)(255 - Math.Min(255, planes[0][sampleOffset] + planes[3][sampleOffset]));
                        green = (byte)(255 - Math.Min(255, planes[1][sampleOffset] + planes[3][sampleOffset]));
                        blue = (byte)(255 - Math.Min(255, planes[2][sampleOffset] + planes[3][sampleOffset]));
                    }
                    else if (format.Layout == RawLayout.RgbaPlanar8)
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
                    WriteScaled(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        value);
                }
            }
        }
    }

    private static void DecodeStrippedYCbCrSubsampled(
        Tiff image,
        string path,
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        double scale,
        RawFormat format,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var offsets = ReadIfdTagValues(path, TiffTag.STRIPOFFSETS);
        if (offsets.Length == 0)
        {
            throw Corrupt(path, "The TIFF has no strip offsets.");
        }

        var rowsPerStripValues = ReadIfdTagValues(path, TiffTag.ROWSPERSTRIP);
        var rowsPerStrip = rowsPerStripValues.Length > 0 ? rowsPerStripValues[0] : height;
        if (rowsPerStrip <= 0 || rowsPerStrip > height)
        {
            rowsPerStrip = height;
        }

        var subX = Math.Max(1, format.SubX);
        var subY = Math.Max(1, format.SubY);
        var blocksAcross = (width + subX - 1) / subX;
        var blockBytes = subX * subY + 2;
        var blockRowBytes = blocksAcross * blockBytes;
        var step = SampleStep(scale);

        var compression = TagInt(image, TiffTag.COMPRESSION);
        if (compression != 1)
        {
            var stripSize = image.StripSize();
            if (stripSize <= 0 || stripSize > MaximumRasterBytes)
            {
                throw Corrupt(path, "The compressed YCbCr strip is too large.");
            }

            var strip = new byte[stripSize];
            for (var stripIndex = 0; stripIndex < offsets.Length; stripIndex++)
            {
                var stripRow = stripIndex * (int)rowsPerStrip;
                if (stripRow >= height)
                {
                    break;
                }

                var rowCount = Math.Min((int)rowsPerStrip, height - stripRow);
                var read = image.ReadEncodedStrip(stripIndex, strip, 0, strip.Length);
                if (read <= 0)
                {
                    throw Corrupt(path, "The compressed YCbCr strip could not be decoded.");
                }

                DecodeYCbCrStripBuffer(
                    strip,
                    read,
                    width,
                    height,
                    stripRow,
                    rowCount,
                    targetWidth,
                    targetHeight,
                    subX,
                    subY,
                    step,
                    pixels,
                    cancellationToken);
            }

            return;
        }

        var blockRow = new byte[blockRowBytes];
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        for (var strip = 0; strip < offsets.Length; strip++)
        {
            var stripRow = strip * (int)rowsPerStrip;
            if (stripRow >= height)
            {
                break;
            }

            var rowCount = Math.Min((int)rowsPerStrip, height - stripRow);
            stream.Position = offsets[strip];
            var blockRows = (rowCount + subY - 1) / subY;
            for (var blockRowIndex = 0; blockRowIndex < blockRows; blockRowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    stream.ReadExactly(blockRow);
                }
                catch (EndOfStreamException)
                {
                    throw Corrupt(path, "The TIFF YCbCr strip ended unexpectedly.");
                }

                for (var dy = 0; dy < subY; dy++)
                {
                    var sourceY = stripRow + blockRowIndex * subY + dy;
                    if (sourceY >= height || sourceY % step != 0)
                    {
                        continue;
                    }

                    for (var sourceX = 0; sourceX < width; sourceX += step)
                    {
                        var blockX = sourceX / subX;
                        var withinX = sourceX % subX;
                        var blockOffset = blockX * blockBytes;
                        var yOffset = blockOffset + dy * subX + withinX;
                        var cbOffset = blockOffset + subX * subY;
                        if (cbOffset + 1 >= blockRow.Length)
                        {
                            continue;
                        }

                        WriteScaled(
                            pixels,
                            targetWidth,
                            targetHeight,
                            sourceX,
                            sourceY,
                            width,
                            height,
                            FromYCbCr(blockRow[yOffset], blockRow[cbOffset], blockRow[cbOffset + 1]));
                    }
                }
            }
        }
    }

    private static void DecodeYCbCrStripBuffer(
        byte[] strip,
        int read,
        int width,
        int height,
        int stripRow,
        int rowCount,
        int targetWidth,
        int targetHeight,
        int subX,
        int subY,
        int step,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var blocksAcross = (width + subX - 1) / subX;
        var blockBytes = subX * subY + 2;
        var blockRowBytes = blocksAcross * blockBytes;
        var blockRows = (rowCount + subY - 1) / subY;
        for (var blockRowIndex = 0; blockRowIndex < blockRows; blockRowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blockRowOffset = blockRowIndex * blockRowBytes;
            for (var dy = 0; dy < subY; dy++)
            {
                var sourceY = stripRow + blockRowIndex * subY + dy;
                if (sourceY >= height || sourceY % step != 0)
                {
                    continue;
                }

                for (var sourceX = 0; sourceX < width; sourceX += step)
                {
                    var blockOffset = blockRowOffset + (sourceX / subX) * blockBytes;
                    var yOffset = blockOffset + dy * subX + (sourceX % subX);
                    var cbOffset = blockOffset + subX * subY;
                    if (cbOffset + 1 >= read || cbOffset + 1 >= strip.Length)
                    {
                        continue;
                    }

                    WriteScaled(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        FromYCbCr(strip[yOffset], strip[cbOffset], strip[cbOffset + 1]));
                }
            }
        }
    }

    private static float ReadSingleNative(byte[] source, int offset)
    {
        var bits = BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4))
            : BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static byte FromFloat(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static float ReadHalfNative(byte[] source, int offset) =>
        (float)BitConverter.UInt16BitsToHalf(ReadUInt16Native(source, offset));

    private static byte Signed16ToByte(ushort value) =>
        (byte)Math.Clamp((unchecked((short)value) + 32768) >> 8, 0, 255);

    private static ushort ReadUInt16Native(byte[] row, int offset)
    {
        // LibTiff returns 16-bit scanline samples in the machine byte order.
        return BitConverter.IsLittleEndian
            ? (ushort)(row[offset] | (row[offset + 1] << 8))
            : (ushort)((row[offset] << 8) | row[offset + 1]);
    }

    private static int RawPixelBytes(RawLayout layout) =>
        layout switch
        {
            RawLayout.None => 0,
            RawLayout.Rgb or RawLayout.RgbPlanar8 or RawLayout.YCbCr8 => 3,
            RawLayout.Rgb16 or RawLayout.RgbPlanar16 => 6,
            RawLayout.Cmyk8 or RawLayout.Rgba8 or RawLayout.CmykPlanar8 or RawLayout.RgbaPlanar8 => 4,
            RawLayout.RgbaPlanar16 => 8,
            RawLayout.Rgba16 => 8,
            RawLayout.Float32Gray or RawLayout.Float32GrayInverted => 4,
            RawLayout.Float32Rgb => 12,
            RawLayout.Float16Gray or RawLayout.Float16GrayInverted => 2,
            RawLayout.Float16Rgb => 6,
            RawLayout.Gray16Int => 2,
            RawLayout.Rgb16Int => 6,
            RawLayout.Palette16 => 2,
            RawLayout.Gray or RawLayout.GrayInverted or RawLayout.Palette8 => 1,
            _ => 2
        };

    private static uint ReadRawPixel(byte[] source, int offset, RawFormat format)
    {
        switch (format.Layout)
        {
            case RawLayout.Rgb:
                return Opaque(source[offset], source[offset + 1], source[offset + 2]);
            case RawLayout.Rgb16:
                return Opaque(
                    (byte)(ReadUInt16Native(source, offset) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 2) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 4) >> 8));
            case RawLayout.Cmyk8:
                var cyan = source[offset];
                var magenta = source[offset + 1];
                var yellow = source[offset + 2];
                var black = source[offset + 3];
                return Opaque(
                    (byte)(255 - Math.Min(255, cyan + black)),
                    (byte)(255 - Math.Min(255, magenta + black)),
                    (byte)(255 - Math.Min(255, yellow + black)));
            case RawLayout.Rgba8:
                return PremultipliedBgra(
                    source[offset],
                    source[offset + 1],
                    source[offset + 2],
                    source[offset + 3]);
            case RawLayout.Rgba16:
                return PremultipliedBgra(
                    (byte)(ReadUInt16Native(source, offset) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 2) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 4) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 6) >> 8));
            case RawLayout.Float32Gray:
                var grayFloat = FromFloat(ReadSingleNative(source, offset));
                return Opaque(grayFloat, grayFloat, grayFloat);
            case RawLayout.Float32GrayInverted:
                var invertedFloat = (byte)(255 - FromFloat(ReadSingleNative(source, offset)));
                return Opaque(invertedFloat, invertedFloat, invertedFloat);
            case RawLayout.Float32Rgb:
                return Opaque(
                    FromFloat(ReadSingleNative(source, offset)),
                    FromFloat(ReadSingleNative(source, offset + 4)),
                    FromFloat(ReadSingleNative(source, offset + 8)));
            case RawLayout.Float16Gray:
                var grayHalf = FromFloat(ReadHalfNative(source, offset));
                return Opaque(grayHalf, grayHalf, grayHalf);
            case RawLayout.Float16GrayInverted:
                var invertedHalf = (byte)(255 - FromFloat(ReadHalfNative(source, offset)));
                return Opaque(invertedHalf, invertedHalf, invertedHalf);
            case RawLayout.Float16Rgb:
                return Opaque(
                    FromFloat(ReadHalfNative(source, offset)),
                    FromFloat(ReadHalfNative(source, offset + 2)),
                    FromFloat(ReadHalfNative(source, offset + 4)));
            case RawLayout.Gray16Int:
                var graySigned = Signed16ToByte(ReadUInt16Native(source, offset));
                return Opaque(graySigned, graySigned, graySigned);
            case RawLayout.Rgb16Int:
                return Opaque(
                    Signed16ToByte(ReadUInt16Native(source, offset)),
                    Signed16ToByte(ReadUInt16Native(source, offset + 2)),
                    Signed16ToByte(ReadUInt16Native(source, offset + 4)));
            case RawLayout.Palette8:
                var palette = format.Palette!;
                var index = source[offset];
                return Opaque(
                    (byte)(palette[index] >> 8),
                    (byte)(palette[256 + index] >> 8),
                    (byte)(palette[512 + index] >> 8));
            case RawLayout.Palette16:
                var palette16 = format.Palette!;
                var index16 = ReadUInt16Native(source, offset);
                return Opaque(
                    (byte)(palette16[index16] >> 8),
                    (byte)(palette16[65536 + index16] >> 8),
                    (byte)(palette16[131072 + index16] >> 8));
            case RawLayout.YCbCr8:
                return FromYCbCr(source[offset], source[offset + 1], source[offset + 2]);
            case RawLayout.Gray:
                return Opaque(source[offset], source[offset], source[offset]);
            case RawLayout.Gray16:
                var gray16 = (byte)(ReadUInt16Native(source, offset) >> 8);
                return Opaque(gray16, gray16, gray16);
            case RawLayout.GrayInverted:
                var inverted = (byte)(255 - source[offset]);
                return Opaque(inverted, inverted, inverted);
            case RawLayout.Gray16Inverted:
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

    private static void WriteScaledValue(
        byte[] pixels,
        int targetWidth,
        int targetHeight,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int value)
    {
        var bgra = ((uint)Tiff.GetA(value) << 24) |
                   ((uint)Tiff.GetR(value) << 16) |
                   ((uint)Tiff.GetG(value) << 8) |
                   (uint)Tiff.GetB(value);
        WriteScaled(pixels, targetWidth, targetHeight, sourceX, sourceY, sourceWidth, sourceHeight, bgra);
    }

    private static void WriteScaled(
        byte[] pixels,
        int targetWidth,
        int targetHeight,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint value)
    {
        if (sourceWidth >= targetWidth && sourceHeight >= targetHeight)
        {
            // Downscale or 1:1: one sample writes one target pixel.
            var x = (int)((long)sourceX * targetWidth / sourceWidth);
            var y = (int)((long)sourceY * targetHeight / sourceHeight);
            WriteBgra(pixels, (y * targetWidth + x) * 4, value);
            return;
        }

        WriteScaledBlock(pixels, targetWidth, targetHeight, sourceX, sourceY, sourceWidth, sourceHeight, value);
    }

    private static void WriteScaledBlock(
        byte[] pixels,
        int targetWidth,
        int targetHeight,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint value)
    {
        var x0 = (int)((long)sourceX * targetWidth / sourceWidth);
        var x1 = (int)((long)(sourceX + 1) * targetWidth / sourceWidth);
        var y0 = (int)((long)sourceY * targetHeight / sourceHeight);
        var y1 = (int)((long)(sourceY + 1) * targetHeight / sourceHeight);
        if (x1 <= x0)
        {
            x1 = x0 + 1;
        }

        if (y1 <= y0)
        {
            y1 = y0 + 1;
        }

        for (var y = y0; y < y1; y++)
        {
            var row = y * targetWidth;
            for (var x = x0; x < x1; x++)
            {
                WriteBgra(pixels, (row + x) * 4, value);
            }
        }
    }

    private static void WriteBgra(byte[] pixels, int offset, uint value)
    {
        // One 32-bit write per pixel keeps the parallel tile/strip workers from
        // tearing a pixel into mixed channels. The last writer wins, which is
        // fine for a proxy preview.
        Volatile.Write(ref Unsafe.As<byte, uint>(ref pixels[offset]), value);
    }

    private static RawFormat GetRawFormat(Tiff image)
    {
        var bits = TagInt(image, TiffTag.BITSPERSAMPLE);
        if (bits is not (8 or 16 or 32))
        {
            return default;
        }

        var samples = TagInt(image, TiffTag.SAMPLESPERPIXEL);
        if (samples == 0)
        {
            samples = 1;
        }

        var planar = TagInt(image, TiffTag.PLANARCONFIG);
        if (planar == 2)
        {
            var planarCompression = TagInt(image, TiffTag.COMPRESSION);
            var planarPhoto = TagInt(image, TiffTag.PHOTOMETRIC);
            if (planarCompression is 1 or 5 or 8 or 32946 or 32773 && !image.IsTiled())
            {
                if (bits == 8 && samples == 3 && planarPhoto == 2)
                {
                    return new RawFormat(RawLayout.RgbPlanar8, null);
                }

                if (bits == 16 && samples == 3 && planarPhoto == 2)
                {
                    return new RawFormat(RawLayout.RgbPlanar16, null);
                }

                if (bits == 8 && samples == 4 && planarPhoto == 5)
                {
                    return new RawFormat(RawLayout.CmykPlanar8, null);
                }

                if (bits == 8 && samples == 4 && planarPhoto == 2)
                {
                    return new RawFormat(RawLayout.RgbaPlanar8, null);
                }

                if (bits == 16 && samples == 4 && planarPhoto == 2)
                {
                    return new RawFormat(RawLayout.RgbaPlanar16, null);
                }
            }

            return default;
        }

        if (planar is not (0 or 1))
        {
            return default;
        }

        var compression = TagInt(image, TiffTag.COMPRESSION);
        if (compression is not (1 or 5 or 7 or 8 or 32946 or 32773))
        {
            return default;
        }

        if (bits == 16)
        {
            var sampleFormat = TagInt(image, TiffTag.SAMPLEFORMAT);
            if (sampleFormat is not (0 or 1 or 2 or 3))
            {
                return default;
            }
        }

        var photometric = TagInt(image, TiffTag.PHOTOMETRIC);
        if (bits == 32 && TagInt(image, TiffTag.SAMPLEFORMAT) == 3 && compression != 7)
        {
            if (samples == 3 && photometric == 2)
            {
                return new RawFormat(RawLayout.Float32Rgb, null);
            }

            if (samples == 1 && photometric == 1)
            {
                return new RawFormat(RawLayout.Float32Gray, null);
            }

            if (samples == 1 && photometric == 0)
            {
                return new RawFormat(RawLayout.Float32GrayInverted, null);
            }
        }

        if (bits == 16 && TagInt(image, TiffTag.SAMPLEFORMAT) == 2 && compression != 7)
        {
            if (samples == 3 && photometric == 2)
            {
                return new RawFormat(RawLayout.Rgb16Int, null);
            }

            if (samples == 1 && photometric == 1)
            {
                return new RawFormat(RawLayout.Gray16Int, null);
            }

            return default;
        }

        if (bits == 16 && TagInt(image, TiffTag.SAMPLEFORMAT) == 3 && compression != 7)
        {
            if (samples == 3 && photometric == 2)
            {
                return new RawFormat(RawLayout.Float16Rgb, null);
            }

            if (samples == 1 && photometric == 1)
            {
                return new RawFormat(RawLayout.Float16Gray, null);
            }

            if (samples == 1 && photometric == 0)
            {
                return new RawFormat(RawLayout.Float16GrayInverted, null);
            }
        }

        if (bits == 8 && samples == 3 && photometric == 6 &&
            compression is 1 or 5 or 8 or 32946 or 32773 && !image.IsTiled())
        {
            var (subX, subY) = ReadSubsampling(image);
            if (subX > 1 || subY > 1)
            {
                return new RawFormat(RawLayout.YCbCrSub8, null, subX, subY);
            }
        }

        if (bits == 8 && samples == 3 && photometric == 6 && compression == 7)
        {
            EnableJpegRgbMode(image);
            return new RawFormat(RawLayout.Rgb, null);
        }

        if (bits == 8 && samples == 3 && photometric == 6 &&
            compression != 7 && HasUnitSubsampling(image))
        {
            return new RawFormat(RawLayout.YCbCr8, null);
        }

        if (bits == 8 && samples == 4 && photometric == 5)
        {
            return new RawFormat(RawLayout.Cmyk8, null);
        }

        if (bits == 8 && samples == 1 && photometric == 3 && compression != 7)
        {
            var palette = ReadColorMap(image);
            if (palette is { Length: >= 768 })
            {
                return new RawFormat(RawLayout.Palette8, palette);
            }

            return default;
        }

        if (bits == 16 && samples == 1 && photometric == 3 && compression != 7)
        {
            var palette = ReadColorMap(image);
            if (palette is { Length: >= 3 * 65536 })
            {
                return new RawFormat(RawLayout.Palette16, palette);
            }

            return default;
        }

        if (bits == 8 && samples == 4 && photometric == 2 && compression != 7 &&
            image.GetField(TiffTag.EXTRASAMPLES) is not null)
        {
            return new RawFormat(RawLayout.Rgba8, null);
        }

        if (bits == 16 && samples == 4 && photometric == 2 && compression != 7)
        {
            return new RawFormat(RawLayout.Rgba16, null);
        }

        if (samples == 3 && photometric == 2)
        {
            return new RawFormat(bits == 16 ? RawLayout.Rgb16 : RawLayout.Rgb, null);
        }

        if (samples == 1 && photometric == 1)
        {
            return new RawFormat(bits == 16 ? RawLayout.Gray16 : RawLayout.Gray, null);
        }

        if (samples == 1 && photometric == 0)
        {
            return new RawFormat(bits == 16 ? RawLayout.Gray16Inverted : RawLayout.GrayInverted, null);
        }

        return default;
    }

    private static ushort[]? ReadColorMap(Tiff image)
    {
        var field = image.GetField(TiffTag.COLORMAP);
        if (field is null)
        {
            return null;
        }

        var parts = new List<ushort[]>();
        foreach (var value in field)
        {
            switch (value.Value)
            {
                case ushort[] bytes:
                    parts.Add(bytes);
                    break;
                case short[] shorts:
                {
                    var map = new ushort[shorts.Length];
                    for (var index = 0; index < shorts.Length; index++)
                    {
                        map[index] = unchecked((ushort)shorts[index]);
                    }

                    parts.Add(map);
                    break;
                }
                case int[] ints:
                {
                    var map = new ushort[ints.Length];
                    for (var index = 0; index < ints.Length; index++)
                    {
                        map[index] = (ushort)Math.Clamp(ints[index], 0, ushort.MaxValue);
                    }

                    parts.Add(map);
                    break;
                }
            }
        }

        if (parts.Count >= 3)
        {
            var length = Math.Min(parts[0].Length, Math.Min(parts[1].Length, parts[2].Length));
            var combined = new ushort[3 * length];
            for (var channel = 0; channel < 3; channel++)
            {
                Array.Copy(parts[channel], 0, combined, channel * length, length);
            }

            return combined;
        }

        return parts.Count == 1 ? parts[0] : null;
    }

    private readonly record struct RawFormat(
        RawLayout Layout,
        ushort[]? Palette,
        int SubX = 1,
        int SubY = 1);

    private enum RawLayout
    {
        None,
        Rgb,
        Rgb16,
        RgbPlanar8,
        RgbPlanar16,
        CmykPlanar8,
        RgbaPlanar8,
        RgbaPlanar16,
        Cmyk8,
        Rgba8,
        Rgba16,
        Float32Gray,
        Float32GrayInverted,
        Float32Rgb,
        Float16Gray,
        Float16GrayInverted,
        Float16Rgb,
        Gray16Int,
        Rgb16Int,
        Palette8,
        Palette16,
        YCbCr8,
        YCbCrSub8,
        Gray,
        Gray16,
        GrayInverted,
        Gray16Inverted
    }

    private static (int Width, int Height, double Scale) ComputeTarget(
        int width,
        int height,
        int maxWidth,
        int maxHeight)
    {
        var limitWidth = maxWidth > 0 ? Math.Min(maxWidth, MaximumPreviewEdge) : MaximumPreviewEdge;
        var limitHeight = maxHeight > 0 ? Math.Min(maxHeight, MaximumPreviewEdge) : MaximumPreviewEdge;
        var scale = Math.Min(1.0, Math.Min(limitWidth / (double)width, limitHeight / (double)height));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        if ((long)targetWidth * targetHeight > int.MaxValue / 4)
        {
            throw new ImageLoadException(
                ImageLoadFailure.TooLarge,
                "The TIFF preview would be too large.");
        }

        return (targetWidth, targetHeight, scale);
    }

    // Roughly two samples per target pixel and axis. The value stays at or below
    // one over the scale, so every target pixel receives at least one sample.
    private static int SampleStep(double scale) =>
        Math.Max(1, (int)Math.Round(0.5 / scale, MidpointRounding.AwayFromZero));

    private static int RoundUp(int value, int step) =>
        step <= 1 ? value : ((value + step - 1) / step) * step;

    private static (int X, int Y) ReadSubsampling(Tiff image)
    {
        var field = image.GetField(TiffTag.YCBCRSUBSAMPLING);
        return field is { Length: >= 2 }
            ? (field[0].ToInt(), field[1].ToInt())
            : (0, 0);
    }

    private static bool HasUnitSubsampling(Tiff image)
    {
        var (x, y) = ReadSubsampling(image);
        return x == 1 && y == 1;
    }

    private static uint FromYCbCr(byte y, byte cb, byte cr)
    {
        var blueDifference = cb - 128;
        var redDifference = cr - 128;
        var red = ClampByte(y + ((91881 * redDifference) >> 16));
        var green = ClampByte(y - ((22554 * blueDifference + 46802 * redDifference) >> 16));
        var blue = ClampByte(y + ((116130 * blueDifference) >> 16));
        return Opaque((byte)red, (byte)green, (byte)blue);
    }

    private static int ClampByte(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    private static void EnableJpegRgbMode(Tiff image)
    {
        if (TagInt(image, TiffTag.COMPRESSION) == 7 &&
            TagInt(image, TiffTag.PHOTOMETRIC) == 6 &&
            TagInt(image, TiffTag.SAMPLESPERPIXEL) == 3)
        {
            image.SetField(TiffTag.JPEGCOLORMODE, JpegColorMode.RGB);
        }
    }

    private static Tiff OpenAtLevel(string path, short directory, long subIfdOffset)
    {
        var image = Tiff.Open(path, "r");
        if (image is null)
        {
            throw Corrupt(path, "The TIFF could not be opened.");
        }

        try
        {
            var switched = subIfdOffset > 0
                ? image.SetSubDirectory(subIfdOffset)
                : image.SetDirectory(directory);
            if (!switched)
            {
                throw Corrupt(path, "The TIFF level could not be opened.");
            }

            ForceStoredOrientation(image);
            EnableJpegRgbMode(image);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static Tiff OpenRaw(string path)
    {
        var image = Tiff.Open(path, "r");
        if (image is null)
        {
            throw Corrupt(path, "The TIFF could not be opened.");
        }

        try
        {
            // LibTiff's RGBA readers apply the orientation tag. The large TIFF
            // path works in the stored raster, so ask for that order. The tag is
            // changed in the in-memory directory only; the file is not rewritten.
            var orientation = TagInt(image, TiffTag.ORIENTATION);
            if (orientation is > 1 and <= 8)
            {
                image.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
            }

            EnableJpegRgbMode(image);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static ImageLoadException Corrupt(string path, string message) =>
        new(ImageLoadFailure.Corrupt, message, path, Path.GetExtension(path));

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

    private static byte[]? TagBytes(Tiff image, TiffTag tag)
    {
        var field = image.GetField(tag);
        return field is { Length: > 0 } ? field[0].ToByteArray() : null;
    }
}

