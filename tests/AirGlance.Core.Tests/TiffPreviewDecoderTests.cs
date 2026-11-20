using AirGlance.Core.Imaging;
using BitMiracle.LibTiff.Classic;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class TiffPreviewDecoderTests
{
    [Fact]
    public void Preview_scales_a_tiled_tiff()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: true, width: 320, height: 200, tileSize: 64, rowsPerStrip: 0);
            AssertPreview(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_scales_a_stripped_tiff()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: false, width: 320, height: 200, tileSize: 0, rowsPerStrip: 32);
            AssertPreview(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_handles_a_single_strip()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: false, width: 320, height: 200, tileSize: 0, rowsPerStrip: 200);
            AssertPreview(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_handles_tiled_edge_tiles()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: true, width: 300, height: 190, tileSize: 64, rowsPerStrip: 0);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 75, 48);

            Assert.Equal(75, bitmap.Width);
            Assert.Equal(48, bitmap.Height);
            Assert.True(bitmap.IsProxy);
            Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
            Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
            Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
            Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_keeps_the_stored_orientation()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(
                path,
                tiled: false,
                width: 160,
                height: 80,
                tileSize: 0,
                rowsPerStrip: 40,
                orientation: Orientation.RIGHTTOP);

            var metadata = TiffPreviewDecoder.ReadMetadata(path);
            Assert.Equal(160, metadata.RawWidth);
            Assert.Equal(80, metadata.RawHeight);
            Assert.Equal(160, metadata.DisplayWidth);
            Assert.Equal(80, metadata.DisplayHeight);
            Assert.Equal(ImageOrientation.TopLeft, metadata.Orientation);

            var bitmap = TiffPreviewDecoder.DecodePreview(path, 40, 20);
            Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_preserves_alpha()
    {
        var path = TemporaryTiff();
        try
        {
            CreateAlphaTiff(path);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 60, 40);

            Assert.Equal(60, bitmap.Width);
            Assert.Equal(40, bitmap.Height);
            Assert.True(bitmap.HasAlpha);
            Assert.Equal((byte)0, Pixel(bitmap, 1, 1).A);
            Assert.Equal((byte)255, Pixel(bitmap, 30, 20).A);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_clamps_to_the_maximum_preview_edge()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: false, width: 320, height: 200, tileSize: 0, rowsPerStrip: 40);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 20_000, 20_000);

            Assert.Equal(320, bitmap.Width);
            Assert.Equal(200, bitmap.Height);
            Assert.Equal(1.0, bitmap.Scale);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Metadata_reports_dimensions_depth_and_format()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: false, width: 320, height: 200, tileSize: 0, rowsPerStrip: 40);
            var metadata = TiffPreviewDecoder.ReadMetadata(path);

            Assert.Equal("TIFF", metadata.FormatName);
            Assert.Equal(320, metadata.RawWidth);
            Assert.Equal(200, metadata.RawHeight);
            Assert.Equal(320, metadata.DisplayWidth);
            Assert.Equal(200, metadata.DisplayHeight);
            Assert.Equal(8, metadata.BitDepth);
            Assert.Null(metadata.IccDescription);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_parallel_tiled_keeps_the_corner_colours()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: true, width: 2048, height: 1536, tileSize: 256, rowsPerStrip: 0);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 512, 384);

            Assert.Equal(512, bitmap.Width);
            Assert.Equal(384, bitmap.Height);
            Assert.Equal("red", Classify(Pixel(bitmap, 2, 2)));
            Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 3, 2)));
            Assert.Equal("blue", Classify(Pixel(bitmap, 2, bitmap.Height - 3)));
            Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 3, bitmap.Height - 3)));
            for (var index = 0; index < bitmap.Width * bitmap.Height; index++)
            {
                Assert.Equal(255, bitmap.Pixels[index * 4 + 3]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_parallel_stripped_keeps_the_corner_colours()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: false, width: 2048, height: 1536, tileSize: 0, rowsPerStrip: 32);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 512, 384);

            Assert.Equal(512, bitmap.Width);
            Assert.Equal(384, bitmap.Height);
            Assert.Equal("red", Classify(Pixel(bitmap, 2, 2)));
            Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 3, 2)));
            Assert.Equal("blue", Classify(Pixel(bitmap, 2, bitmap.Height - 3)));
            Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 3, bitmap.Height - 3)));
            for (var index = 0; index < bitmap.Width * bitmap.Height; index++)
            {
                Assert.Equal(255, bitmap.Pixels[index * 4 + 3]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_honours_cancellation()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: true, width: 2048, height: 1536, tileSize: 256, rowsPerStrip: 0);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => TiffPreviewDecoder.DecodePreview(path, 512, 384, source.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_uses_a_reduced_top_level_directory()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTwoLevelTiff(path);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 200, 150);

            Assert.Equal(200, bitmap.Width);
            Assert.Equal(150, bitmap.Height);
            Assert.Equal(800, bitmap.SourceWidth);
            Assert.Equal(600, bitmap.SourceHeight);

            // The reduced directory is green; the full directory is red.
            Assert.Equal("green", Classify(Pixel(bitmap, 100, 75)));

            var metadata = TiffPreviewDecoder.ReadMetadata(path);
            Assert.Equal(800, metadata.RawWidth);
            Assert.Equal(600, metadata.RawHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_parallels_a_reduced_directory()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTwoLevelTiff(path, 4096, 3072, 2048, 1536);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 1024, 768);

            Assert.Equal(1024, bitmap.Width);
            Assert.Equal(768, bitmap.Height);
            Assert.Equal(4096, bitmap.SourceWidth);
            Assert.Equal(3072, bitmap.SourceHeight);

            // The 2048 x 1536 reduced directory is above the 2 MP parallel
            // threshold and is green; worker handles must open it, not dir 0.
            Assert.Equal("green", Classify(Pixel(bitmap, 512, 384)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TemporaryTiff() =>
        Path.Combine(Path.GetTempPath(), "airglance-preview-" + Guid.NewGuid().ToString("N") + ".tif");

    private static void AssertPreview(string path)
    {
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal(80, bitmap.Width);
        Assert.Equal(50, bitmap.Height);
        Assert.Equal(320, bitmap.SourceWidth);
        Assert.Equal(200, bitmap.SourceHeight);
        Assert.Equal(0.25, bitmap.Scale, 6);
        Assert.True(bitmap.IsProxy);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));

        for (var index = 0; index < bitmap.Width * bitmap.Height; index++)
        {
            Assert.Equal(255, bitmap.Pixels[index * 4 + 3]);
        }
    }

    private static (byte B, byte G, byte R, byte A) Pixel(DecodedBitmap bitmap, int x, int y)
    {
        var offset = (y * bitmap.Width + x) * 4;
        return (bitmap.Pixels[offset], bitmap.Pixels[offset + 1], bitmap.Pixels[offset + 2], bitmap.Pixels[offset + 3]);
    }

    private static string Classify((byte B, byte G, byte R, byte A) pixel)
    {
        var (b, g, r, _) = pixel;
        if (r > 140 && g < 130 && b < 130)
        {
            return "red";
        }

        if (g > 120 && r < 130 && b < 160)
        {
            return "green";
        }

        if (b > 140 && r < 130 && g < 160)
        {
            return "blue";
        }

        if (r > 140 && g > 140 && b < 130)
        {
            return "yellow";
        }

        return $"unknown({r},{g},{b})";
    }

    private static void CreateTiff(
        string path,
        bool tiled,
        int width,
        int height,
        int tileSize,
        int rowsPerStrip,
        Orientation orientation = Orientation.TOPLEFT)
    {
        using var tiff = Tiff.Open(path, "w");
        Assert.NotNull(tiff);
        SetCommonFields(tiff, width, height, samplesPerPixel: 3, orientation);

        if (tiled)
        {
            tiff.SetField(TiffTag.TILEWIDTH, tileSize);
            tiff.SetField(TiffTag.TILELENGTH, tileSize);
            var tile = new byte[tileSize * tileSize * 3];
            var tilesAcross = (width + tileSize - 1) / tileSize;
            var tilesDown = (height + tileSize - 1) / tileSize;
            for (var tileY = 0; tileY < tilesDown; tileY++)
            {
                for (var tileX = 0; tileX < tilesAcross; tileX++)
                {
                    Array.Clear(tile);
                    for (var y = 0; y < tileSize; y++)
                    {
                        for (var x = 0; x < tileSize; x++)
                        {
                            var imageX = tileX * tileSize + x;
                            var imageY = tileY * tileSize + y;
                            if (imageX >= width || imageY >= height)
                            {
                                continue;
                            }

                            var (red, green, blue) = Pattern(imageX, imageY, width, height);
                            var offset = (y * tileSize + x) * 3;
                            tile[offset] = red;
                            tile[offset + 1] = green;
                            tile[offset + 2] = blue;
                        }
                    }

                    tiff.WriteEncodedTile(tileY * tilesAcross + tileX, tile, 0, tile.Length);
                }
            }

            return;
        }

        tiff.SetField(TiffTag.ROWSPERSTRIP, rowsPerStrip);
        var rowBytes = new byte[width * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = Pattern(x, y, width, height);
                rowBytes[x * 3] = red;
                rowBytes[x * 3 + 1] = green;
                rowBytes[x * 3 + 2] = blue;
            }

            tiff.WriteScanline(rowBytes, y);
        }
    }

    private static void CreateTwoLevelTiff(string path) =>
        CreateTwoLevelTiff(path, 800, 600, 400, 300);

    private static void CreateTwoLevelTiff(
        string path,
        int fullWidth,
        int fullHeight,
        int reducedWidth,
        int reducedHeight)
    {
        using var tiff = Tiff.Open(path, "w");
        Assert.NotNull(tiff);
        WriteSolidDirectory(tiff, fullWidth, fullHeight, 255, 0, 0);
        tiff.WriteDirectory();
        WriteSolidDirectory(tiff, reducedWidth, reducedHeight, 0, 160, 60);
    }

    private static void WriteSolidDirectory(Tiff tiff, int width, int height, byte red, byte green, byte blue)
    {
        SetCommonFields(tiff, width, height, samplesPerPixel: 3, Orientation.TOPLEFT);
        tiff.SetField(TiffTag.ROWSPERSTRIP, 64);
        var row = new byte[width * 3];
        for (var x = 0; x < width; x++)
        {
            row[x * 3] = red;
            row[x * 3 + 1] = green;
            row[x * 3 + 2] = blue;
        }

        for (var y = 0; y < height; y++)
        {
            tiff.WriteScanline(row, y);
        }
    }

    private static void CreateAlphaTiff(string path)
    {
        const int width = 120;
        const int height = 80;
        using var tiff = Tiff.Open(path, "w");
        Assert.NotNull(tiff);
        SetCommonFields(tiff, width, height, samplesPerPixel: 4, Orientation.TOPLEFT);
        tiff.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.UNASSALPHA });
        tiff.SetField(TiffTag.ROWSPERSTRIP, 20);
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = x is >= 40 and < 80 && y is >= 25 and < 55;
                row[x * 4] = inside ? (byte)227 : (byte)0;
                row[x * 4 + 1] = inside ? (byte)86 : (byte)0;
                row[x * 4 + 2] = inside ? (byte)59 : (byte)0;
                row[x * 4 + 3] = inside ? (byte)255 : (byte)0;
            }

            tiff.WriteScanline(row, y);
        }
    }

    private static void SetCommonFields(Tiff tiff, int width, int height, int samplesPerPixel, Orientation orientation)
    {
        tiff.SetField(TiffTag.IMAGEWIDTH, width);
        tiff.SetField(TiffTag.IMAGELENGTH, height);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, samplesPerPixel);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
        tiff.SetField(TiffTag.ORIENTATION, orientation);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        tiff.SetField(TiffTag.COMPRESSION, Compression.LZW);
    }

    private static (byte Red, byte Green, byte Blue) Pattern(int x, int y, int width, int height)
    {
        var left = x < width / 2;
        var top = y < height / 2;
        return (left, top) switch
        {
            (true, true) => ((byte)255, (byte)0, (byte)0),
            (false, true) => ((byte)0, (byte)160, (byte)60),
            (true, false) => ((byte)0, (byte)90, (byte)255),
            _ => ((byte)255, (byte)220, (byte)0)
        };
    }
}
