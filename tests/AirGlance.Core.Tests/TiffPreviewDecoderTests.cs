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

    [Fact]
    public void Preview_upscales_a_slightly_smaller_reduced_directory()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTwoLevelTiff(path, 4096, 3072, 2048, 1536);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 2100, 1575);

            Assert.Equal(2100, bitmap.Width);
            Assert.Equal(1575, bitmap.Height);
            Assert.Equal(4096, bitmap.SourceWidth);
            Assert.Equal(3072, bitmap.SourceHeight);

            // 2048 is within the 95% tolerance of 2100, so the reduced green
            // directory is upscaled into the target without holes.
            Assert.Equal("green", Classify(Pixel(bitmap, 1050, 787)));
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
    public void Preview_skips_a_page_and_uses_the_reduced_directory()
    {
        var path = TemporaryTiff();
        try
        {
            CreateThreeLevelTiff(path);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 200, 150);

            Assert.Equal(200, bitmap.Width);
            Assert.Equal(150, bitmap.Height);
            // dir 1 is a page (green), dir 2 is reduced (blue). The page must
            // not win the smaller-area selection.
            Assert.Equal("blue", Classify(Pixel(bitmap, 100, 75)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_uses_a_subifd_level()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "subifd-levels.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 100, 75);

        Assert.Equal(100, bitmap.Width);
        Assert.Equal(75, bitmap.Height);
        Assert.Equal(400, bitmap.SourceWidth);
        Assert.Equal(300, bitmap.SourceHeight);

        // The 100 x 75 subIFD is blue; the full directory is red and the
        // 200 x 150 subIFD is green.
        Assert.Equal("blue", Classify(Pixel(bitmap, 50, 37)));
    }

    [Fact]
    public void Preview_uses_16bit_scanlines_for_a_single_strip_tiff()
    {
        var path = TemporaryTiff();
        try
        {
            // 4200 x 4000 x RGBA is above the 64 MiB strip-raster cap, so the
            // RGBA strip path cannot be used; this must go through scanlines.
            CreateSixteenBitTiff(path, 4200, 4000);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 105, 100);

            Assert.Equal(105, bitmap.Width);
            Assert.Equal(100, bitmap.Height);
            Assert.Equal(4200, bitmap.SourceWidth);
            Assert.Equal(4000, bitmap.SourceHeight);
            Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
            Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
            Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
            Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));

            var metadata = TiffPreviewDecoder.ReadMetadata(path);
            Assert.Equal(16, metadata.BitDepth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_uses_16bit_raw_tiles()
    {
        var path = TemporaryTiff();
        try
        {
            CreateSixteenBitTiff(path, 2048, 1536, tiled: true, tileSize: 256);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 512, 384);

            Assert.Equal(512, bitmap.Width);
            Assert.Equal(384, bitmap.Height);
            Assert.Equal("red", Classify(Pixel(bitmap, 2, 2)));
            Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 3, 2)));
            Assert.Equal("blue", Classify(Pixel(bitmap, 2, bitmap.Height - 3)));
            Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 3, bitmap.Height - 3)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preview_uses_16bit_raw_strips()
    {
        var path = TemporaryTiff();
        try
        {
            CreateSixteenBitTiff(path, 2048, 1536, tiled: false, rowsPerStrip: 32);
            var bitmap = TiffPreviewDecoder.DecodePreview(path, 512, 384);

            Assert.Equal(512, bitmap.Width);
            Assert.Equal(384, bitmap.Height);
            Assert.Equal("red", Classify(Pixel(bitmap, 2, 2)));
            Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 3, 2)));
            Assert.Equal("blue", Classify(Pixel(bitmap, 2, bitmap.Height - 3)));
            Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 3, bitmap.Height - 3)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("cmyk-quadrants.tif")]
    [InlineData("rgb-quadrants-jpeg.tif")]
    public void Preview_decodes_cmyk_and_rgb_jpeg_quadrants(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal(320, bitmap.SourceWidth);
        Assert.Equal(200, bitmap.SourceHeight);
        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
        Assert.Equal(8, TiffPreviewDecoder.ReadMetadata(path).BitDepth);
    }

    [Fact]
    public void Preview_decodes_an_oversized_cmyk_single_strip()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "cmyk-large-single-strip.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 105, 100);

        Assert.Equal(4200, bitmap.SourceWidth);
        Assert.Equal(4000, bitmap.SourceHeight);
        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_decodes_16bit_palette_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "palette16-quadrants.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_decodes_float32_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "float32-quadrants.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_decodes_planar_rgb_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "planar-rgb.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_preserves_alpha_for_16bit_rgba_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "rgba16-quadrants.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        var red = Pixel(bitmap, 1, 1);
        Assert.Equal("red", Classify(red));
        Assert.Equal(255, red.A);
        var green = Pixel(bitmap, bitmap.Width - 2, 1);
        Assert.Equal("green", Classify(green));
        Assert.Equal(156, green.A);
        var blue = Pixel(bitmap, 1, bitmap.Height - 2);
        Assert.Equal("blue", Classify(blue));
        Assert.Equal(156, blue.A);
        var yellow = Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2);
        Assert.Equal("yellow", Classify(yellow));
        Assert.Equal(255, yellow.A);
    }

    [Theory]
    [InlineData("ycbcr-subsampled-lzw.tif")]
    [InlineData("ycbcr-subsampled-deflate.tif")]
    public void Preview_decodes_compressed_subsampled_ycbcr(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_decodes_subsampled_ycbcr_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ycbcr-subsampled.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_decodes_non_jpeg_ycbcr_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ycbcr-quadrants.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
    }

    [Fact]
    public void Preview_decodes_cmyk_jpeg_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "cmyk-quadrants-jpeg.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 512, 384);

        Assert.Equal(2048, bitmap.SourceWidth);
        Assert.Equal(1536, bitmap.SourceHeight);
        Assert.Equal("red", Classify(Pixel(bitmap, 2, 2)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 3, 2)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 2, bitmap.Height - 3)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 3, bitmap.Height - 3)));
    }

    [Fact]
    public void Preview_decodes_ycbcr_jpeg_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ycbcr-quadrants-jpeg.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 512, 384);

        Assert.Equal(2048, bitmap.SourceWidth);
        Assert.Equal(1536, bitmap.SourceHeight);
        Assert.Equal("red", Classify(Pixel(bitmap, 2, 2)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 3, 2)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 2, bitmap.Height - 3)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 3, bitmap.Height - 3)));
    }

    [Fact]
    public void Preview_decodes_palette_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "palette-quadrants.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        Assert.Equal("red", Classify(Pixel(bitmap, 1, 1)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 2, 1)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 1, bitmap.Height - 2)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2)));
        Assert.Equal(255, Pixel(bitmap, 1, 1).A);
    }

    [Fact]
    public void Preview_preserves_alpha_for_rgba_quadrants()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "rgba-quadrants.tif");
        var bitmap = TiffPreviewDecoder.DecodePreview(path, 80, 50);

        var red = Pixel(bitmap, 1, 1);
        Assert.Equal("red", Classify(red));
        Assert.Equal(255, red.A);
        var green = Pixel(bitmap, bitmap.Width - 2, 1);
        Assert.Equal("green", Classify(green));
        Assert.Equal(200, green.A);
        var blue = Pixel(bitmap, 1, bitmap.Height - 2);
        Assert.Equal("blue", Classify(blue));
        Assert.Equal(255, blue.A);
        var yellow = Pixel(bitmap, bitmap.Width - 2, bitmap.Height - 2);
        Assert.Equal("yellow", Classify(yellow));
        Assert.Equal(200, yellow.A);
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

    private static void CreateThreeLevelTiff(string path)
    {
        using var tiff = Tiff.Open(path, "w");
        Assert.NotNull(tiff);
        WriteSolidDirectory(tiff, 800, 600, 255, 0, 0);
        tiff.WriteDirectory();
        WriteSolidDirectory(tiff, 400, 300, 0, 160, 60, subfileType: 2);
        tiff.WriteDirectory();
        WriteSolidDirectory(tiff, 400, 300, 0, 90, 255, subfileType: 1);
    }

    private static void WriteSolidDirectory(
        Tiff tiff,
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        int? subfileType = null)
    {
        SetCommonFields(tiff, width, height, samplesPerPixel: 3, Orientation.TOPLEFT);
        if (subfileType is { } value)
        {
            tiff.SetField(TiffTag.SUBFILETYPE, value);
        }

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

    private static void CreateSixteenBitTiff(
        string path,
        int width,
        int height,
        bool tiled = false,
        int tileSize = 256,
        int rowsPerStrip = 0)
    {
        using var tiff = Tiff.Open(path, "w");
        Assert.NotNull(tiff);
        tiff.SetField(TiffTag.IMAGEWIDTH, width);
        tiff.SetField(TiffTag.IMAGELENGTH, height);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, 3);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 16);
        tiff.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
        tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        tiff.SetField(TiffTag.COMPRESSION, Compression.LZW);
        if (tiled)
        {
            tiff.SetField(TiffTag.TILEWIDTH, tileSize);
            tiff.SetField(TiffTag.TILELENGTH, tileSize);
            var tile = new byte[tileSize * tileSize * 6];
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

                            var (red, green, blue) = Pattern16(imageX, imageY, width, height);
                            var offset = (y * tileSize + x) * 6;
                            WriteUInt16Native(tile, offset, red);
                            WriteUInt16Native(tile, offset + 2, green);
                            WriteUInt16Native(tile, offset + 4, blue);
                        }
                    }

                    tiff.WriteEncodedTile(tileY * tilesAcross + tileX, tile, 0, tile.Length);
                }
            }

            return;
        }

        tiff.SetField(TiffTag.ROWSPERSTRIP, rowsPerStrip > 0 ? rowsPerStrip : height);
        var row = new byte[width * 6];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = Pattern16(x, y, width, height);
                var offset = x * 6;
                WriteUInt16Native(row, offset, red);
                WriteUInt16Native(row, offset + 2, green);
                WriteUInt16Native(row, offset + 4, blue);
            }

            tiff.WriteScanline(row, y);
        }
    }

    private static void WriteUInt16Native(byte[] buffer, int offset, ushort value)
    {
        if (BitConverter.IsLittleEndian)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }
        else
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }
    }

    private static (ushort Red, ushort Green, ushort Blue) Pattern16(int x, int y, int width, int height)
    {
        var left = x < width / 2;
        var top = y < height / 2;
        return (left, top) switch
        {
            (true, true) => ((ushort)65535, (ushort)0, (ushort)0),
            (false, true) => ((ushort)0, (ushort)40000, (ushort)10000),
            (true, false) => ((ushort)0, (ushort)20000, (ushort)65535),
            _ => ((ushort)65535, (ushort)50000, (ushort)0)
        };
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
