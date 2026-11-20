using System.Runtime.InteropServices;
using AirGlance.Core.Imaging;
using BitMiracle.LibTiff.Classic;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class TiffRegionReaderTests
{
    [Fact]
    public void Region_reader_returns_bgra_tile_from_a_stripped_tiff()
    {
        using var reader = TiffRegionReader.Open(Asset("rgb-lzw.tif"));

        Assert.Equal(80, reader.PixelWidth);
        Assert.Equal(40, reader.PixelHeight);

        var bytes = reader.ReadTile(0, 0, 4, 4);

        Assert.Equal(4 * 4 * 4, bytes.Length);
        for (var index = 0; index < 4 * 4; index++)
        {
            Assert.Equal(255, bytes[index * 4 + 3]);
            // The top-left corner of rgb-lzw.tif is red.
            Assert.True(bytes[index * 4 + 2] > 200, "red channel");
            Assert.True(bytes[index * 4 + 1] < 60, "green channel");
            Assert.True(bytes[index * 4] < 60, "blue channel");
        }
    }

    [Fact]
    public void Region_reader_matches_the_full_decode_of_a_stripped_fixture()
    {
        AssertMatchesFullDecode(Asset("rgb-lzw.tif"), 16);
    }

    [Fact]
    public void Region_reader_matches_the_full_decode_of_an_alpha_fixture()
    {
        AssertMatchesFullDecode(Asset("alpha.tif"), 8);
    }

    [Fact]
    public void Region_reader_matches_the_full_decode_of_a_multi_strip_image()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: false, width: 60, height: 44, rowsPerStrip: 10);
            AssertTopLeftIsRed(path);
            AssertMatchesFullDecode(path, 16);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Region_reader_matches_the_full_decode_of_a_tiled_image()
    {
        var path = TemporaryTiff();
        try
        {
            CreateTiff(path, tiled: true, width: 60, height: 44, rowsPerStrip: 0);
            AssertTopLeftIsRed(path);
            AssertMatchesFullDecode(path, 16);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Region_reader_clamps_a_tile_at_the_image_edge()
    {
        using var reader = TiffRegionReader.Open(Asset("rgb-lzw.tif"));

        var bytes = reader.ReadTile(76, 36, 16, 16);

        Assert.Equal(4 * 4 * 4, bytes.Length);
        for (var index = 0; index < 4 * 4; index++)
        {
            Assert.Equal(255, bytes[index * 4 + 3]);
        }
    }

    [Fact]
    public void Region_reader_matches_the_raw_decode_of_an_oriented_tiff()
    {
        var path = Asset("orientation-6.tif");
        using var reader = TiffRegionReader.Open(path);
        using var raw = ImageDecoder.DecodeFullRaw(path);
        var expected = new byte[raw.Width * raw.Height * 4];
        Marshal.Copy(raw.Pixels, expected, 0, expected.Length);

        Assert.Equal(raw.Width, reader.PixelWidth);
        Assert.Equal(raw.Height, reader.PixelHeight);

        var actual = reader.ReadTile(0, 0, 4, 4);
        // The region path returns the stored raster; ImageTransform applies the
        // orientation when the tile cache is written.
        Assert.True(actual[2] > 200, "stored top-left is red");
        Assert.True(actual[1] < 60, "green channel");
        Assert.True(actual[0] < 60, "blue channel");

        const int tileSize = 16;
        for (var y = 0; y < raw.Height; y += tileSize)
        {
            for (var x = 0; x < raw.Width; x += tileSize)
            {
                var width = Math.Min(tileSize, raw.Width - x);
                var height = Math.Min(tileSize, raw.Height - y);
                var tile = reader.ReadTile(x, y, width, height);
                for (var row = 0; row < height; row++)
                {
                    for (var column = 0; column < width; column++)
                    {
                        var source = ((y + row) * raw.Width + x + column) * 4;
                        var target = (row * width + column) * 4;
                        Assert.Equal(expected[source], tile[target]);
                        Assert.Equal(expected[source + 1], tile[target + 1]);
                        Assert.Equal(expected[source + 2], tile[target + 2]);
                        Assert.Equal(expected[source + 3], tile[target + 3]);
                    }
                }
            }
        }
    }

    [Fact]
    public void Disposed_reader_rejects_reads()
    {
        var reader = TiffRegionReader.Open(Asset("rgb-lzw.tif"));
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.ReadTile(0, 0, 1, 1));
        reader.Dispose();
    }

    private static void AssertSixteenBitCorners(string path)
    {
        using var reader = TiffRegionReader.Open(path);
        Assert.Equal(320, reader.PixelWidth);
        Assert.Equal(200, reader.PixelHeight);

        var top = reader.ReadTile(0, 0, 4, 4);
        for (var index = 0; index < 4 * 4; index++)
        {
            Assert.Equal(255, top[index * 4 + 3]);
            Assert.True(top[index * 4 + 2] > 200, "red channel");
            Assert.True(top[index * 4 + 1] < 60, "green channel");
            Assert.True(top[index * 4] < 60, "blue channel");
        }

        var bottom = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        for (var index = 0; index < 4 * 4; index++)
        {
            Assert.Equal(255, bottom[index * 4 + 3]);
            Assert.True(bottom[index * 4] > 200, "blue channel");
            Assert.True(bottom[index * 4 + 2] < 60, "red channel");
        }
    }

    private static void CreateSixteenBitTiff(
        string path,
        bool tiled,
        int width,
        int height,
        int tileSize,
        int rowsPerStrip)
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

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    [Fact]
    public void Region_reader_reads_16bit_tiles()
    {
        var path = TemporaryTiff();
        try
        {
            CreateSixteenBitTiff(path, tiled: true, width: 320, height: 200, tileSize: 64, rowsPerStrip: 0);
            AssertSixteenBitCorners(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Region_reader_reads_16bit_strips()
    {
        var path = TemporaryTiff();
        try
        {
            CreateSixteenBitTiff(path, tiled: false, width: 320, height: 200, tileSize: 0, rowsPerStrip: 32);
            AssertSixteenBitCorners(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Region_reader_reads_a_16bit_single_strip()
    {
        var path = TemporaryTiff();
        try
        {
            CreateSixteenBitTiff(path, tiled: false, width: 320, height: 200, tileSize: 0, rowsPerStrip: 200);
            AssertSixteenBitCorners(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Region_reader_streams_an_oversized_single_strip()
    {
        var path = TemporaryTiff();
        try
        {
            // 4200 x 4000 x 6 bytes is above the 64 MiB strip cap, so the
            // reader has to build the temporary BGRA row cache.
            CreateSixteenBitTiff(path, tiled: false, width: 4200, height: 4000, tileSize: 0, rowsPerStrip: 4000);
            using var reader = TiffRegionReader.Open(path);
            Assert.Equal(4200, reader.PixelWidth);
            Assert.Equal(4000, reader.PixelHeight);
            Assert.True(reader.RequiresStreamingCache);

            var progress = new List<double>();
            reader.CacheProgressChanged += value => progress.Add(value);
            reader.Prepare();
            Assert.NotEmpty(progress);
            Assert.True(progress.Count > 10);
            Assert.Equal(1, progress[^1]);
            for (var index = 1; index < progress.Count; index++)
            {
                Assert.True(progress[index] >= progress[index - 1]);
            }

            var top = reader.ReadTile(0, 0, 4, 4);
            for (var index = 0; index < 4 * 4; index++)
            {
                Assert.Equal(255, top[index * 4 + 3]);
                Assert.True(top[index * 4 + 2] > 200, "red channel");
                Assert.True(top[index * 4] < 60, "blue channel");
            }

            var bottom = reader.ReadTile(0, 3996, 4, 4);
            for (var index = 0; index < 4 * 4; index++)
            {
                Assert.Equal(255, bottom[index * 4 + 3]);
                Assert.True(bottom[index * 4] > 200, "blue channel");
                Assert.True(bottom[index * 4 + 2] < 60, "red channel");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("cmyk-quadrants.tif")]
    [InlineData("rgb-quadrants-jpeg.tif")]
    public void Region_reader_decodes_cmyk_and_rgb_jpeg_quadrants(string name)
    {
        using var reader = TiffRegionReader.Open(Asset(name));
        Assert.Equal(320, reader.PixelWidth);
        Assert.Equal(200, reader.PixelHeight);

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
        foreach (var tile in new[] { topLeft, topRight, bottomLeft, bottomRight })
        {
            for (var index = 0; index < 16; index++)
            {
                Assert.Equal(255, tile[index * 4 + 3]);
            }
        }
    }

    [Fact]
    public void Region_reader_streams_an_oversized_cmyk_single_strip()
    {
        using var reader = TiffRegionReader.Open(Asset("cmyk-large-single-strip.tif"));
        Assert.Equal(4200, reader.PixelWidth);
        Assert.Equal(4000, reader.PixelHeight);
        Assert.True(reader.RequiresStreamingCache);

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_decodes_16bit_palette_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("palette16-quadrants.tif"));
        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_decodes_float32_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("float32-quadrants.tif"));
        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_streams_planar_rgb()
    {
        using var reader = TiffRegionReader.Open(Asset("planar-rgb.tif"));
        Assert.True(reader.RequiresStreamingCache);
        reader.Prepare();

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_preserves_alpha_for_16bit_rgba_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("rgba16-quadrants.tif"));
        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
        Assert.Equal(255, topLeft[3]);
        Assert.Equal(156, topRight[3]);
        Assert.Equal(156, bottomLeft[3]);
        Assert.Equal(255, bottomRight[3]);
    }

    [Theory]
    [InlineData("ycbcr-subsampled-lzw.tif")]
    [InlineData("ycbcr-subsampled-deflate.tif")]
    public void Region_reader_streams_compressed_subsampled_ycbcr(string name)
    {
        using var reader = TiffRegionReader.Open(Asset(name));
        Assert.True(reader.RequiresStreamingCache);
        reader.Prepare();

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_streams_subsampled_ycbcr()
    {
        using var reader = TiffRegionReader.Open(Asset("ycbcr-subsampled.tif"));
        Assert.True(reader.RequiresStreamingCache);
        reader.Prepare();

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_decodes_non_jpeg_ycbcr_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("ycbcr-quadrants.tif"));
        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_decodes_cmyk_jpeg_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("cmyk-quadrants-jpeg.tif"));
        Assert.Equal(2048, reader.PixelWidth);
        Assert.Equal(1536, reader.PixelHeight);

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_decodes_ycbcr_jpeg_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("ycbcr-quadrants-jpeg.tif"));
        Assert.Equal(2048, reader.PixelWidth);
        Assert.Equal(1536, reader.PixelHeight);

        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
    }

    [Fact]
    public void Region_reader_decodes_palette_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("palette-quadrants.tif"));
        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
        Assert.Equal(255, topLeft[3]);
    }

    [Fact]
    public void Region_reader_preserves_alpha_for_rgba_quadrants()
    {
        using var reader = TiffRegionReader.Open(Asset("rgba-quadrants.tif"));
        var topLeft = reader.ReadTile(0, 0, 4, 4);
        var topRight = reader.ReadTile(reader.PixelWidth - 4, 0, 4, 4);
        var bottomLeft = reader.ReadTile(0, reader.PixelHeight - 4, 4, 4);
        var bottomRight = reader.ReadTile(reader.PixelWidth - 4, reader.PixelHeight - 4, 4, 4);
        Assert.Equal("red", ClassifyTile(topLeft, 0));
        Assert.Equal("green", ClassifyTile(topRight, 0));
        Assert.Equal("blue", ClassifyTile(bottomLeft, 0));
        Assert.Equal("yellow", ClassifyTile(bottomRight, 0));
        Assert.Equal(255, topLeft[3]);
        Assert.Equal(200, topRight[3]);
        Assert.Equal(255, bottomLeft[3]);
        Assert.Equal(200, bottomRight[3]);
    }

    private static string ClassifyTile(byte[] bgra, int pixel)
    {
        var offset = pixel * 4;
        var blue = bgra[offset];
        var green = bgra[offset + 1];
        var red = bgra[offset + 2];
        if (red > 140 && green < 130 && blue < 130)
        {
            return "red";
        }

        if (green > 120 && red < 130 && blue < 160)
        {
            return "green";
        }

        if (blue > 140 && red < 130 && green < 160)
        {
            return "blue";
        }

        if (red > 140 && green > 140 && blue < 130)
        {
            return "yellow";
        }

        return $"unknown({red},{green},{blue})";
    }

    private static string TemporaryTiff() =>
        Path.Combine(Path.GetTempPath(), "airglance-region-" + Guid.NewGuid().ToString("N") + ".tif");

    private static void AssertTopLeftIsRed(string path)
    {
        var bitmap = ImageDecoder.DecodeFull(path);
        var offset = (6 * bitmap.Width + 6) * 4;
        Assert.True(bitmap.Pixels[offset + 2] > 200, "red channel");
        Assert.True(bitmap.Pixels[offset + 1] < 60, "green channel");
        Assert.True(bitmap.Pixels[offset] < 60, "blue channel");
    }

    private static void AssertMatchesFullDecode(string path, int tileSize)
    {
        using var reader = TiffRegionReader.Open(path);
        var expected = ImageDecoder.DecodeFull(path);

        Assert.Equal(expected.Width, reader.PixelWidth);
        Assert.Equal(expected.Height, reader.PixelHeight);

        for (var y = 0; y < expected.Height; y += tileSize)
        {
            for (var x = 0; x < expected.Width; x += tileSize)
            {
                var width = Math.Min(tileSize, expected.Width - x);
                var height = Math.Min(tileSize, expected.Height - y);
                var actual = reader.ReadTile(x, y, width, height);

                Assert.Equal(width * height * 4, actual.Length);
                for (var row = 0; row < height; row++)
                {
                    for (var column = 0; column < width; column++)
                    {
                        var source = ((y + row) * expected.Width + x + column) * 4;
                        var target = (row * width + column) * 4;
                        Assert.Equal(expected.Pixels[source], actual[target]);
                        Assert.Equal(expected.Pixels[source + 1], actual[target + 1]);
                        Assert.Equal(expected.Pixels[source + 2], actual[target + 2]);
                        Assert.Equal(expected.Pixels[source + 3], actual[target + 3]);
                    }
                }
            }
        }
    }

    private static void CreateTiff(string path, bool tiled, int width, int height, int rowsPerStrip)
    {
        using var tiff = Tiff.Open(path, "w");
        Assert.NotNull(tiff);
        tiff.SetField(TiffTag.IMAGEWIDTH, width);
        tiff.SetField(TiffTag.IMAGELENGTH, height);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, 3);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
        tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        tiff.SetField(TiffTag.COMPRESSION, Compression.LZW);

        if (tiled)
        {
            const int tileSize = 16;
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
