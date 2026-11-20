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
            var layout = GetRawLayout(image);

            if (image.IsTiled())
            {
                if (layout == RawLayout.None)
                {
                    DecodeTiledRgba(image, path, width, height, targetWidth, targetHeight, scale, pixels, cancellationToken);
                }
                else
                {
                    DecodeTiledRaw(image, path, width, height, targetWidth, targetHeight, scale, layout, pixels, cancellationToken);
                }
            }
            else
            {
                if (layout == RawLayout.None)
                {
                    DecodeStrippedRgba(image, path, width, height, targetWidth, targetHeight, scale, pixels, cancellationToken);
                }
                else
                {
                    DecodeStrippedRaw(image, path, width, height, targetWidth, targetHeight, scale, layout, pixels, cancellationToken);
                }
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
                    var targetY = (int)((long)sourceY * targetHeight / height);
                    var firstColumn = RoundUp(tileX, step);
                    for (var sourceX = firstColumn; sourceX < tileX + columnCount; sourceX += step)
                    {
                        var targetX = (int)((long)sourceX * targetWidth / width);
                        WritePixel(pixels, targetWidth, targetX, targetY, raster[rowStart + sourceX - tileX]);
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
                var targetY = (int)((long)sourceY * targetHeight / height);
                for (var sourceX = 0; sourceX < width; sourceX += step)
                {
                    var targetX = (int)((long)sourceX * targetWidth / width);
                    WritePixel(pixels, targetWidth, targetX, targetY, raster[rowStart + sourceX]);
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
                    var targetY = (int)((long)sourceY * targetHeight / height);
                    var rowOffset = (sourceY - tileY) * tileWidth;
                    var firstColumn = RoundUp(tileX, step);
                    for (var sourceX = firstColumn; sourceX < tileX + columnCount; sourceX += step)
                    {
                        var offset = (rowOffset + sourceX - tileX) * bytesPerPixel;
                        if (offset + bytesPerPixel > read)
                        {
                            continue;
                        }

                        var targetX = (int)((long)sourceX * targetWidth / width);
                        if (layout == RawLayout.Rgb)
                        {
                            WriteOpaquePixel(pixels, targetWidth, targetX, targetY, raw[offset], raw[offset + 1], raw[offset + 2]);
                        }
                        else
                        {
                            var gray = raw[offset];
                            if (layout == RawLayout.GrayInverted)
                            {
                                gray = (byte)(255 - gray);
                            }

                            WriteOpaquePixel(pixels, targetWidth, targetX, targetY, gray, gray, gray);
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

            var targetY = (int)((long)sourceY * targetHeight / height);
            for (var sourceX = 0; sourceX < width; sourceX += step)
            {
                var offset = sourceX * bytesPerPixel;
                var targetX = (int)((long)sourceX * targetWidth / width);
                if (layout == RawLayout.Rgb)
                {
                    WriteOpaquePixel(pixels, targetWidth, targetX, targetY, row[offset], row[offset + 1], row[offset + 2]);
                }
                else
                {
                    var gray = row[offset];
                    if (layout == RawLayout.GrayInverted)
                    {
                        gray = (byte)(255 - gray);
                    }

                    WriteOpaquePixel(pixels, targetWidth, targetX, targetY, gray, gray, gray);
                }
            }
        }
    }

    private static void WriteOpaquePixel(
        byte[] pixels,
        int targetWidth,
        int x,
        int y,
        byte red,
        byte green,
        byte blue)
    {
        var offset = (y * targetWidth + x) * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = 255;
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

    private static void WritePixel(byte[] pixels, int targetWidth, int x, int y, int value)
    {
        var offset = (y * targetWidth + x) * 4;
        // LibTiff's RGBA reader returns premultiplied samples.
        pixels[offset] = (byte)Tiff.GetB(value);
        pixels[offset + 1] = (byte)Tiff.GetG(value);
        pixels[offset + 2] = (byte)Tiff.GetR(value);
        pixels[offset + 3] = (byte)Tiff.GetA(value);
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

