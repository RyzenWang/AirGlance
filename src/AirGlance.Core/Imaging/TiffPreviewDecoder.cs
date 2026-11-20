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
        var layout = GetRawLayout(image);
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
                    layout,
                    pixels,
                    directory,
                    subIfdOffset,
                    cancellationToken))
            {
                return;
            }

            if (layout == RawLayout.None)
            {
                DecodeTiledRgba(
                    image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, pixels, cancellationToken);
            }
            else
            {
                DecodeTiledRaw(
                    image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, layout, pixels, cancellationToken);
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
                layout,
                pixels,
                directory,
                subIfdOffset,
                cancellationToken))
        {
            return;
        }

        if (layout == RawLayout.None)
        {
            DecodeStrippedRgba(
                image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, pixels, cancellationToken);
        }
        else
        {
            DecodeStrippedRaw(
                image, path, decodeWidth, decodeHeight, targetWidth, targetHeight, sampleScale, layout, pixels, cancellationToken);
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
        RawLayout layout,
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

        var perWorkerBytes = layout == RawLayout.None
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
                layout,
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
        RawLayout layout,
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
        if (layout == RawLayout.None)
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

        var bytesPerPixel = layout == RawLayout.Rgb ? 3 : 1;
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

                        if (layout == RawLayout.Rgb)
                        {
                            WriteScaledOpaque(
                                pixels,
                                targetWidth,
                                targetHeight,
                                sourceX,
                                sourceY,
                                width,
                                height,
                                raw[offset],
                                raw[offset + 1],
                                raw[offset + 2]);
                        }
                        else
                        {
                            var gray = raw[offset];
                            if (layout == RawLayout.GrayInverted)
                            {
                                gray = (byte)(255 - gray);
                            }

                            WriteScaledOpaque(
                                pixels,
                                targetWidth,
                                targetHeight,
                                sourceX,
                                sourceY,
                                width,
                                height,
                                gray,
                                gray,
                                gray);
                        }
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
        RawLayout layout,
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
        if (layout != RawLayout.None)
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
                    layout,
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
        RawLayout layout,
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
        var bytesPerPixel = layout == RawLayout.Rgb ? 3 : 1;
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

                    if (layout == RawLayout.Rgb)
                    {
                        WriteScaledOpaque(
                            pixels,
                            targetWidth,
                            targetHeight,
                            sourceX,
                            sourceY,
                            width,
                            height,
                            raw[offset],
                            raw[offset + 1],
                            raw[offset + 2]);
                    }
                    else
                    {
                        var gray = raw[offset];
                        if (layout == RawLayout.GrayInverted)
                        {
                            gray = (byte)(255 - gray);
                        }

                        WriteScaledOpaque(
                            pixels,
                            targetWidth,
                            targetHeight,
                            sourceX,
                            sourceY,
                            width,
                            height,
                            gray,
                            gray,
                            gray);
                    }
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
        RawLayout layout,
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

        var bytesPerPixel = layout == RawLayout.Rgb ? 3 : 1;
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

                        if (layout == RawLayout.Rgb)
                        {
                            WriteScaledOpaque(
                                pixels,
                                targetWidth,
                                targetHeight,
                                sourceX,
                                sourceY,
                                width,
                                height,
                                raw[offset],
                                raw[offset + 1],
                                raw[offset + 2]);
                        }
                        else
                        {
                            var gray = raw[offset];
                            if (layout == RawLayout.GrayInverted)
                            {
                                gray = (byte)(255 - gray);
                            }

                            WriteScaledOpaque(
                                pixels,
                                targetWidth,
                                targetHeight,
                                sourceX,
                                sourceY,
                                width,
                                height,
                                gray,
                                gray,
                                gray);
                        }
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
        RawLayout layout,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var bytesPerPixel = layout == RawLayout.Rgb ? 3 : 1;
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
                if (layout == RawLayout.Rgb)
                {
                    WriteScaledOpaque(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        row[offset],
                        row[offset + 1],
                        row[offset + 2]);
                }
                else
                {
                    var gray = row[offset];
                    if (layout == RawLayout.GrayInverted)
                    {
                        gray = (byte)(255 - gray);
                    }

                    WriteScaledOpaque(
                        pixels,
                        targetWidth,
                        targetHeight,
                        sourceX,
                        sourceY,
                        width,
                        height,
                        gray,
                        gray,
                        gray);
                }
            }
        }
    }

    private static void WriteScaledOpaque(
        byte[] pixels,
        int targetWidth,
        int targetHeight,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        byte red,
        byte green,
        byte blue)
    {
        var value = 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        WriteScaled(pixels, targetWidth, targetHeight, sourceX, sourceY, sourceWidth, sourceHeight, value);
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

    private static RawLayout GetRawLayout(Tiff image)
    {
        if (TagInt(image, TiffTag.BITSPERSAMPLE) != 8)
        {
            return RawLayout.None;
        }

        var samples = TagInt(image, TiffTag.SAMPLESPERPIXEL);
        if (samples == 0)
        {
            samples = 1;
        }

        var planar = TagInt(image, TiffTag.PLANARCONFIG);
        if (planar is not (0 or 1))
        {
            return RawLayout.None;
        }

        var compression = TagInt(image, TiffTag.COMPRESSION);
        if (compression is not (1 or 5 or 8 or 32946 or 32773))
        {
            return RawLayout.None;
        }

        var photometric = TagInt(image, TiffTag.PHOTOMETRIC);
        if (samples == 3 && photometric == 2)
        {
            return RawLayout.Rgb;
        }

        if (samples == 1 && photometric == 1)
        {
            return RawLayout.Gray;
        }

        if (samples == 1 && photometric == 0)
        {
            return RawLayout.GrayInverted;
        }

        return RawLayout.None;
    }

    private enum RawLayout
    {
        None,
        Rgb,
        Gray,
        GrayInverted
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

