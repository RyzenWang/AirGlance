using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class ImageDecoderTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

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

        if (g > 80 && g < 140 && r < 100 && b > 70 && b < 130)
        {
            return "teal";
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

    [Theory]
    [InlineData(1, "red", "green", "blue", "yellow")]
    [InlineData(2, "green", "red", "yellow", "blue")]
    [InlineData(3, "yellow", "blue", "green", "red")]
    [InlineData(4, "blue", "yellow", "red", "green")]
    [InlineData(5, "red", "blue", "green", "yellow")]
    [InlineData(6, "blue", "red", "yellow", "green")]
    [InlineData(7, "yellow", "green", "blue", "red")]
    [InlineData(8, "green", "yellow", "red", "blue")]
    public void Exif_orientations_are_applied_to_decoded_pixels(
        int orientation,
        string topLeft,
        string topRight,
        string bottomLeft,
        string bottomRight)
    {
        var bitmap = ImageDecoder.DecodeFull(Asset($"orientation-{orientation}.jpg"));
        var swapped = orientation >= 5;
        Assert.Equal(swapped ? 40 : 80, bitmap.Width);
        Assert.Equal(swapped ? 80 : 40, bitmap.Height);
        Assert.Equal(swapped ? 40 : 80, bitmap.SourceWidth);
        Assert.Equal(swapped ? 80 : 40, bitmap.SourceHeight);

        var width = bitmap.Width;
        var height = bitmap.Height;

        Assert.Equal(topLeft, Classify(Pixel(bitmap, 6, 6)));
        Assert.Equal(topRight, Classify(Pixel(bitmap, width - 7, 6)));
        Assert.Equal(bottomLeft, Classify(Pixel(bitmap, 6, height - 7)));
        Assert.Equal(bottomRight, Classify(Pixel(bitmap, width - 7, height - 7)));
    }

    [Fact]
    public void Scaled_decode_respects_the_requested_box()
    {
        var bitmap = ImageDecoder.Decode(Asset("orientation-1.jpg"), 20, 20);

        Assert.True(bitmap.Width <= 20);
        Assert.True(bitmap.Height <= 20);
        Assert.Equal(80, bitmap.SourceWidth);
        Assert.Equal(40, bitmap.SourceHeight);
        Assert.InRange(bitmap.Scale, 0.2, 0.3);
    }

    [Fact]
    public void Full_decode_has_scale_one()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("orientation-1.jpg"));

        Assert.Equal(80, bitmap.Width);
        Assert.True(bitmap.IsFullResolution);
        Assert.Equal(1.0, bitmap.Scale, 6);
    }

    [Fact]
    public void Png_alpha_is_preserved_and_premultiplied()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("alpha.png"));

        Assert.Equal(48, bitmap.Width);
        Assert.Equal(32, bitmap.Height);
        Assert.Equal((byte)0, Pixel(bitmap, 0, 0).A);
        Assert.Equal((byte)255, Pixel(bitmap, 24, 16).A);
    }

    [Fact]
    public void Gif_decodes_its_first_frame()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("animated.gif"));

        Assert.Equal(40, bitmap.Width);
        Assert.Equal(40, bitmap.Height);
        Assert.Equal("red", Classify(Pixel(bitmap, 20, 20)));
    }

    [Fact]
    public void Svg_renders_at_its_intrinsic_size()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("vector.svg"));

        Assert.Equal(64, bitmap.Width);
        Assert.Equal(48, bitmap.Height);
        Assert.True(bitmap.HasAlpha);
        Assert.Equal("red", Classify(Pixel(bitmap, 8, 8)));
        var teal = Pixel(bitmap, 32, 32);
        Assert.Equal((byte)99, teal.B);
        Assert.Equal((byte)111, teal.G);
        Assert.Equal((byte)46, teal.R);
        Assert.Equal((byte)0, Pixel(bitmap, 0, 47).A);
    }

    [Fact]
    public void Svg_scaled_decode_keeps_the_vector_aspect_ratio()
    {
        var bitmap = ImageDecoder.Decode(Asset("vector.svg"), 32, 32);

        Assert.Equal(32, bitmap.Width);
        Assert.Equal(24, bitmap.Height);
        Assert.Equal(64, bitmap.SourceWidth);
        Assert.Equal(48, bitmap.SourceHeight);
    }

    [Fact]
    public void Svg_metadata_and_format_are_reported()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("vector.svg"));

        Assert.Equal(64, metadata.DisplayWidth);
        Assert.Equal(48, metadata.DisplayHeight);
        Assert.Equal("SVG", metadata.FormatName);
        Assert.False(metadata.IsAnimated);
    }

    [Fact]
    public void Animated_gif_decodes_every_frame_and_delay()
    {
        var animation = ImageDecoder.DecodeAnimation(Asset("animated.gif"), 40, 40);

        Assert.NotNull(animation);
        Assert.Equal(3, animation.FrameCount);
        Assert.Equal([100, 100, 100], animation.DelaysMilliseconds);
        Assert.Equal(40, animation.Frames[0].Width);
        Assert.Equal("red", Classify(Pixel(animation.Frames[0], 20, 20)));
        Assert.Equal("teal", Classify(Pixel(animation.Frames[1], 20, 20)));
        Assert.Equal("yellow", Classify(Pixel(animation.Frames[2], 20, 20)));
    }

    [Fact]
    public void Animated_gif_metadata_reports_frame_count()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("animated.gif"));

        Assert.Equal(3, metadata.FrameCount);
        Assert.True(metadata.IsAnimated);
    }

    [Fact]
    public void Still_image_has_no_animation()
    {
        Assert.Null(ImageDecoder.DecodeAnimation(Asset("alpha.png"), 40, 40));
    }

    [Fact]
    public void Tiff_decodes_rgb_and_lzw()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("rgb-lzw.tif"));

        Assert.Equal(80, bitmap.Width);
        Assert.Equal(40, bitmap.Height);
        Assert.Equal("red", Classify(Pixel(bitmap, 6, 6)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 7, 6)));
        Assert.Equal("blue", Classify(Pixel(bitmap, 6, bitmap.Height - 7)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, bitmap.Width - 7, bitmap.Height - 7)));
    }

    [Fact]
    public void Avif_is_supported_and_decodes()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The local AVIF decoder uses the Windows libvips native package.
            return;
        }

        Assert.True(SupportedFormats.IsSupported("sample.avif"));
        var path = Asset("sample.avif");
        var metadata = ImageDecoder.ReadMetadata(path);
        Assert.Equal("AVIF", metadata.FormatName);
        Assert.Equal(1440, metadata.DisplayWidth);
        Assert.Equal(900, metadata.DisplayHeight);

        var bitmap = ImageDecoder.Decode(path, 800, 800);
        Assert.True(bitmap.Width <= 800);
        Assert.True(bitmap.Height <= 800);
        Assert.Equal(1440, bitmap.SourceWidth);
        Assert.Equal(900, bitmap.SourceHeight);
    }

    [Fact]
    public void Tiff_region_reader_returns_bgra_tile()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The Plan A native libvips package is currently Windows-only.
            return;
        }

        using var reader = TiffRegionReader.Open(Asset("rgb-lzw.tif"));
        var width = Math.Min(4, reader.PixelWidth);
        var height = Math.Min(4, reader.PixelHeight);
        var bytes = reader.ReadTile(0, 0, width, height);

        Assert.Equal(width * height * 4, bytes.Length);
        for (var index = 0; index < width * height; index++)
        {
            Assert.Equal(255, bytes[index * 4 + 3]);
        }
    }

    [Fact]
    public void Tiff_alpha_is_preserved()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("alpha.tif"));

        Assert.Equal(48, bitmap.Width);
        Assert.Equal(32, bitmap.Height);
        Assert.Equal((byte)0, Pixel(bitmap, 0, 0).A);
        Assert.Equal((byte)255, Pixel(bitmap, 24, 16).A);
    }

    [Fact]
    public void Tiff_orientation_is_applied()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("orientation-6.tif"));

        Assert.Equal(40, bitmap.Width);
        Assert.Equal(80, bitmap.Height);
        Assert.Equal(80, bitmap.SourceWidth);
        Assert.Equal(40, bitmap.SourceHeight);
        Assert.Equal("blue", Classify(Pixel(bitmap, 6, 6)));
        Assert.Equal("red", Classify(Pixel(bitmap, bitmap.Width - 7, 6)));
        Assert.Equal("yellow", Classify(Pixel(bitmap, 6, bitmap.Height - 7)));
        Assert.Equal("green", Classify(Pixel(bitmap, bitmap.Width - 7, bitmap.Height - 7)));
    }

    [Fact]
    public void Tiff_scaled_decode_respects_the_requested_box()
    {
        var bitmap = ImageDecoder.Decode(Asset("rgb-lzw.tif"), 40, 20);

        Assert.Equal(40, bitmap.Width);
        Assert.Equal(20, bitmap.Height);
        Assert.Equal(80, bitmap.SourceWidth);
        Assert.Equal(40, bitmap.SourceHeight);
        Assert.Equal(0.5, bitmap.Scale, 6);
    }

    [Fact]
    public void Tiff_metadata_reports_format_and_dimensions()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("rgb-lzw.tif"));

        Assert.Equal(80, metadata.DisplayWidth);
        Assert.Equal(40, metadata.DisplayHeight);
        Assert.Equal("TIFF", metadata.FormatName);
        Assert.Equal(8, metadata.BitDepth);
    }

    [Fact]
    public void Tiff_raw_decode_carries_the_orientation_for_the_tile_writer()
    {
        using var raw = ImageDecoder.DecodeFullRaw(Asset("orientation-6.tif"));

        Assert.Equal(80, raw.Width);
        Assert.Equal(40, raw.Height);
        Assert.Equal(40, raw.SourceWidth);
        Assert.Equal(80, raw.SourceHeight);
        Assert.Equal(ImageOrientation.RightTop, raw.Orientation);
    }

    [Fact]
    public void Webp_decodes()
    {
        var bitmap = ImageDecoder.DecodeFull(Asset("alpha.webp"));

        Assert.Equal(48, bitmap.Width);
        Assert.Equal(32, bitmap.Height);
        Assert.Equal((byte)0, Pixel(bitmap, 0, 0).A);
    }

    [Fact]
    public void Sixteen_bit_png_reports_its_source_bit_depth()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("gradient-16bit.png"));

        Assert.Equal(16, metadata.BitDepth);
        Assert.Equal(64, metadata.DisplayWidth);
        Assert.Equal(32, metadata.DisplayHeight);
    }

    [Fact]
    public void Metadata_reports_orientation_adjusted_size()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("orientation-6.jpg"));

        Assert.Equal(80, metadata.RawWidth);
        Assert.Equal(40, metadata.RawHeight);
        Assert.Equal(40, metadata.DisplayWidth);
        Assert.Equal(80, metadata.DisplayHeight);
        Assert.Equal(ImageOrientation.RightTop, metadata.Orientation);
        Assert.Equal("JPEG", metadata.FormatName);
    }

    [Fact]
    public void Display_p3_is_converted_to_srgb()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("display-p3.jpg"));
        Assert.NotNull(metadata.IccDescription);
        Assert.Contains("P3", metadata.IccDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sRGB", metadata.ColorSpaceDisplay, StringComparison.OrdinalIgnoreCase);

        var p3 = ImageDecoder.DecodeFull(Asset("display-p3.jpg"));
        var srgb = ImageDecoder.DecodeFull(Asset("srgb.jpg"));

        var p3Pixel = Pixel(p3, 5, 5);
        var srgbPixel = Pixel(srgb, 5, 5);

        // The same numeric colour in a wider space becomes more saturated in sRGB.
        Assert.True(p3Pixel.R > srgbPixel.R + 10, $"P3 red {p3Pixel.R} should exceed sRGB red {srgbPixel.R}");
        Assert.True(p3Pixel.G < srgbPixel.G, $"P3 green {p3Pixel.G} should be below sRGB green {srgbPixel.G}");
    }

    [Fact]
    public void Adobe_rgb_profile_is_named()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("adobe-rgb.jpg"));

        Assert.Equal("Adobe RGB (1998)", metadata.IccDescription);
    }

    [Fact]
    public void Png_icc_profile_is_read()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("display-p3.png"));

        Assert.NotNull(metadata.IccDescription);
        Assert.Contains("P3", metadata.IccDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exif_fields_are_read()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("exif-full.jpg"));
        var exif = metadata.Exif;

        Assert.Equal("AirWorks AirGlance Test Camera", exif.Camera);
        Assert.Equal("AW 35mm f/1.8", exif.LensModel);
        Assert.Equal(1.0 / 250, exif.ExposureSeconds!.Value, 6);
        Assert.Equal(2.8, exif.FNumber!.Value, 3);
        Assert.Equal(200, exif.Iso);
        Assert.Equal(35, exif.FocalLengthMillimeters!.Value, 3);
        Assert.NotNull(exif.TakenAt);
        Assert.Equal(2026, exif.TakenAt!.Value.Year);
        Assert.Equal(11, exif.TakenAt.Value.Month);
        Assert.Equal(17, exif.TakenAt.Value.Day);
        Assert.Equal(10, exif.TakenAt.Value.Hour);
        Assert.Equal(25, exif.TakenAt.Value.Minute);
    }

    [Fact]
    public void Corrupt_file_reports_corrupt()
    {
        var exception = Assert.Throws<ImageLoadException>(
            () => ImageDecoder.DecodeFull(Asset("corrupt.jpg")));

        Assert.Equal(ImageLoadFailure.Corrupt, exception.Failure);
    }

    [Fact]
    public void Empty_png_reports_corrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), "airglance-empty-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, []);

        var exception = Assert.Throws<ImageLoadException>(() => ImageDecoder.ReadMetadata(path));
        Assert.Equal(ImageLoadFailure.Corrupt, exception.Failure);

        File.Delete(path);
    }

    [Fact]
    public void Unsupported_extension_reports_unsupported()
    {
        var path = Path.Combine(Path.GetTempPath(), "airglance-" + Guid.NewGuid().ToString("N") + ".heic");
        File.WriteAllBytes(path, [0, 1, 2, 3]);

        var exception = Assert.Throws<ImageLoadException>(() => ImageDecoder.DecodeFull(path));

        Assert.Equal(ImageLoadFailure.Unsupported, exception.Failure);
        Assert.Equal(".heic", exception.Extension);

        File.Delete(path);
    }

    [Fact]
    public void Missing_file_reports_not_found()
    {
        var exception = Assert.Throws<ImageLoadException>(
            () => ImageDecoder.ReadMetadata(Asset("does-not-exist.jpg")));

        Assert.Equal(ImageLoadFailure.NotFound, exception.Failure);
    }

    [Fact]
    public void Metadata_matches_decoded_pixels()
    {
        var metadata = ImageDecoder.ReadMetadata(Asset("display-p3.jpg"));
        var bitmap = ImageDecoder.DecodeFull(Asset("display-p3.jpg"));

        Assert.Equal(metadata.DisplayWidth, bitmap.Width);
        Assert.Equal(metadata.DisplayHeight, bitmap.Height);
        Assert.Equal(metadata.FileSizeBytes, new FileInfo(Asset("display-p3.jpg")).Length);
    }
}
