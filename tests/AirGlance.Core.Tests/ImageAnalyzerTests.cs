using AirGlance.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class ImageAnalyzerTests
{
    [Fact]
    public void Flat_grey_lands_in_one_bin()
    {
        var bitmap = CreateBitmap(8, 8, (_, _) => (128, 128, 128, 255));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(1_024));

        Assert.Equal(64, result.PixelCount);
        Assert.Equal(64, result.Luma[128]);
        Assert.Equal(128, result.Mean, 3);
        Assert.Equal(128, result.Median);
        Assert.Equal(0, result.StdDev, 3);
        Assert.Equal(1, result.UniqueColorCount);
        Assert.Single(result.Palette);
        Assert.Equal(new RgbColor(128, 128, 128), result.AverageColor);
        Assert.Equal(0, result.ShadowClipCount);
        Assert.Equal(0, result.HighlightClipCount);
    }

    [Fact]
    public void Black_and_white_halves_report_both_clips()
    {
        var bitmap = CreateBitmap(8, 8, (x, _) => x < 4 ? (0, 0, 0, 255) : (255, 255, 255, 255));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(2_048));

        Assert.Equal(64, result.PixelCount);
        Assert.Equal(32, result.ShadowClipCount);
        Assert.Equal(32, result.HighlightClipCount);
        Assert.Equal(32, result.HardBlackCount);
        Assert.Equal(32, result.HardWhiteCount);
        Assert.Equal(32, result.ZoneCounts[0]);
        Assert.Equal(32, result.ZoneCounts[10]);
        Assert.Equal(127.5, result.Mean, 3);
    }

    [Fact]
    public void Transparent_pixels_are_excluded_from_colour_stats()
    {
        var bitmap = CreateBitmap(
            4,
            1,
            (x, _) => x < 2 ? (255, 0, 0, 255) : (0, 0, 0, 0));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(256));

        Assert.Equal(2, result.PixelCount);
        Assert.Equal(2, result.TransparentCount);
        Assert.True(result.HasTransparency);
        Assert.Equal(new RgbColor(255, 0, 0), result.AverageColor);
        Assert.Equal(2, result.Hue[0]);
        Assert.Equal(2, result.Alpha[0]);
        Assert.Equal(2, result.Alpha[255]);
    }

    [Fact]
    public void Semi_transparent_pixels_are_unpremultiplied()
    {
        // Premultiplied red at alpha 128 is (0, 0, 128, 128) in BGRA order.
        var bitmap = CreateRaw(
            1,
            1,
            [(byte)0, 0, 128, 128]);

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(64));

        Assert.Equal(1, result.PixelCount);
        Assert.Equal(1, result.SemiTransparentCount);
        Assert.True(result.AverageColor.R >= 250);
        Assert.Equal(1, result.Alpha[128]);
    }

    [Fact]
    public void Hue_bins_separate_red_green_and_blue()
    {
        var bitmap = CreateBitmap(
            3,
            1,
            (x, _) => x switch
            {
                0 => (255, 0, 0, 255),
                1 => (0, 255, 0, 255),
                _ => (0, 0, 255, 255)
            });

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(512));

        Assert.Equal(1, result.Hue[0]);
        Assert.Equal(1, result.Hue[12]);
        Assert.Equal(1, result.Hue[24]);
    }

    [Fact]
    public void Gradient_produces_one_pixel_per_level()
    {
        var bitmap = CreateBitmap(256, 1, (x, _) => ((byte)x, (byte)x, (byte)x, 255));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(1_024));

        Assert.All(result.Luma, count => Assert.Equal(1, count));
        Assert.Equal(127.5, result.Mean, 3);
        Assert.Equal(127, result.Median);
        Assert.Equal(8, result.Entropy, 2);
    }

    [Fact]
    public void Palette_returns_the_dominant_colours()
    {
        var bitmap = CreateBitmap(
            30,
            10,
            (x, _) => (x / 10) switch
            {
                0 => (220, 40, 40, 255),
                1 => (40, 200, 80, 255),
                _ => (40, 80, 220, 255)
            });

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(4_096));

        Assert.Equal(3, result.Palette.Count);
        Assert.All(result.Palette, colour => Assert.Equal(1.0 / 3, colour.Share, 2));
        Assert.Contains(result.Palette, colour => colour.Color.R > 180 && colour.Color.G < 100);
        Assert.Contains(result.Palette, colour => colour.Color.G > 150 && colour.Color.R < 100);
        Assert.Contains(result.Palette, colour => colour.Color.B > 180 && colour.Color.R < 100);
    }

    [Fact]
    public void One_pixel_image_is_safe()
    {
        var bitmap = CreateBitmap(1, 1, (_, _) => (10, 20, 30, 255));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(8));

        Assert.Equal(1, result.PixelCount);
        Assert.Equal(0, result.Sharpness);
        Assert.Equal(0, result.NoiseSigma);
        Assert.Single(result.Palette);
    }

    [Fact]
    public void Fully_transparent_image_does_not_divide_by_zero()
    {
        var bitmap = CreateBitmap(4, 4, (_, _) => (0, 0, 0, 0));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(16));

        Assert.Equal(0, result.PixelCount);
        Assert.Equal(16, result.TransparentCount);
        Assert.Equal(0, result.Mean);
        Assert.Empty(result.Palette);
        Assert.Equal(0, result.BytesPerPixel);
        Assert.False(double.IsNaN(result.AverageSaturation));
    }

    [Fact]
    public void Opaque_png_with_alpha_channel_is_not_reported_as_transparent()
    {
        var bitmap = CreateBitmap(2, 2, (_, _) => (1, 2, 3, 255));

        var result = ImageAnalyzer.Analyze(bitmap, new ImageAnalysisContext(64));

        Assert.False(result.HasTransparency);
        Assert.Equal(0, result.TransparentCount);
    }

    [Fact]
    public void Jpeg_header_reports_subsampling_and_estimated_quality()
    {
        using var source = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(new SKColor(120, 80, 40));
        }

        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 82);
        Assert.NotNull(data);

        var path = Path.Combine(Path.GetTempPath(), "airglance-jpeg-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            File.WriteAllBytes(path, data!.ToArray());
            var header = ImageHeaderReader.Read(path);

            Assert.NotNull(header.Jpeg);
            Assert.True(header.Jpeg!.Baseline);
            Assert.False(header.Jpeg.Progressive);
            Assert.Contains(header.Jpeg.Subsampling, new[] { "4:2:0", "4:2:2", "4:4:4" });
            Assert.NotNull(header.Jpeg.EstimatedQuality);
            Assert.InRange(header.Jpeg.EstimatedQuality!.Value, 55, 98);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Png_header_has_no_jpeg_info()
    {
        using var bitmap = new SKBitmap(8, 8);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Assert.NotNull(data);

        var path = Path.Combine(Path.GetTempPath(), "airglance-png-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, data!.ToArray());
            var header = ImageHeaderReader.Read(path);
            Assert.Null(header.Jpeg);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DecodedBitmap CreateBitmap(
        int width,
        int height,
        Func<int, int, (int R, int G, int B, int A)> pixel)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var offset = (y * width + x) * 4;
                bytes[offset] = Premultiply((byte)b, (byte)a);
                bytes[offset + 1] = Premultiply((byte)g, (byte)a);
                bytes[offset + 2] = Premultiply((byte)r, (byte)a);
                bytes[offset + 3] = (byte)a;
            }
        }

        return new DecodedBitmap(width, height, bytes, width, height, 1.0) { HasAlpha = true };
    }

    private static DecodedBitmap CreateRaw(int width, int height, byte[] bytes) =>
        new(width, height, bytes, width, height, 1.0) { HasAlpha = true };

    private static byte Premultiply(byte value, byte alpha) =>
        alpha == 255 ? value : (byte)((value * alpha + 127) / 255);
}
