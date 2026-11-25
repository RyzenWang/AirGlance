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

    [Fact]
    public void Avif_is_listed_as_supported()
    {
        Assert.Equal(SupportedFormats.SupportsAvif, SupportedFormats.Extensions.Contains(".avif"));

        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        Assert.True(SupportedFormats.IsSupported("image.avif"));
        Assert.True(SupportedFormats.IsAvif("image.avif"));
        Assert.Equal("AVIF", SupportedFormats.Describe("image.avif"));
    }

    [Fact]
    public void Avif_native_decoder_is_available_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(
            SupportedFormats.SupportsAvif,
            "Build the Windows x64 AVIF decoder with native/avif/build.ps1 before running the tests.");
    }

    [Fact]
    public void Avif_decodes_a_still_image()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var metadata = ImageDecoder.ReadMetadata(Asset("sample.avif"));
        Assert.Equal(1440, metadata.RawWidth);
        Assert.Equal(900, metadata.RawHeight);
        Assert.Equal(1440, metadata.DisplayWidth);
        Assert.Equal(900, metadata.DisplayHeight);
        Assert.Equal("AVIF", metadata.FormatName);
        Assert.Equal(8, metadata.BitDepth);
        Assert.Equal(1, metadata.FrameCount);

        var bitmap = ImageDecoder.DecodeFull(Asset("sample.avif"));
        Assert.Equal(1440, bitmap.Width);
        Assert.Equal(900, bitmap.Height);
        Assert.False(bitmap.HasAlpha);
        Assert.False(bitmap.IsProxy);

        var center = Pixel(bitmap, bitmap.Width / 2, bitmap.Height / 2);
        Assert.InRange(center.B, 133, 143);
        Assert.InRange(center.G, 135, 145);
        Assert.InRange(center.R, 134, 144);
        Assert.Equal(255, center.A);
    }

    [Fact]
    public void Avif_preview_respects_the_requested_box()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var preview = ImageDecoder.Decode(Asset("sample.avif"), 800, 600);

        Assert.Equal(800, preview.Width);
        Assert.Equal(500, preview.Height);
        Assert.Equal(1440, preview.SourceWidth);
        Assert.Equal(900, preview.SourceHeight);
        Assert.Equal(800.0 / 1440.0, preview.Scale, 3);
    }

    [Fact]
    public void Avif_clean_aperture_crops_the_decoded_frame()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var metadata = ImageDecoder.ReadMetadata(Asset("clap.avif"));
        Assert.Equal(1438, metadata.RawWidth);
        Assert.Equal(898, metadata.RawHeight);
        Assert.Equal(1438, metadata.DisplayWidth);
        Assert.Equal(898, metadata.DisplayHeight);

        // The decoder writes the whole 1440 x 900 coded frame and reports the clean
        // aperture separately. Sizing the destination buffer from the crop made every
        // clap file fail as corrupt before the frame could be trimmed.
        var cropped = ImageDecoder.DecodeFull(Asset("clap.avif"));
        Assert.Equal(1438, cropped.Width);
        Assert.Equal(898, cropped.Height);
        Assert.Equal(1438, cropped.SourceWidth);
        Assert.Equal(898, cropped.SourceHeight);

        // The aperture is centred with zero offsets, so it drops the outer row and
        // column: the result must match the same frame one pixel inside.
        var frame = ImageDecoder.DecodeFull(Asset("sample.avif"));
        for (var y = 0; y < cropped.Height; y += 300)
        {
            for (var x = 0; x < cropped.Width; x += 300)
            {
                var inside = Pixel(cropped, x, y);
                var reference = Pixel(frame, x + 1, y + 1);
                Assert.Equal(reference.R, inside.R);
                Assert.Equal(reference.G, inside.G);
                Assert.Equal(reference.B, inside.B);
            }
        }
    }

    [Fact]
    public void Avif_alpha_is_preserved()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var bitmap = ImageDecoder.DecodeFull(Asset("alpha.avif"));

        Assert.Equal(80, bitmap.Width);
        Assert.Equal(80, bitmap.Height);
        Assert.True(bitmap.HasAlpha);
        Assert.Equal(0, Pixel(bitmap, 0, 0).A);
        Assert.Equal(0, Pixel(bitmap, bitmap.Width - 1, bitmap.Height - 1).A);

        var center = Pixel(bitmap, bitmap.Width / 2, bitmap.Height / 2);
        Assert.Equal(255, center.A);
        Assert.InRange(center.B, 245, 255);
        Assert.InRange(center.G, 245, 255);
        Assert.InRange(center.R, 245, 255);
    }

    [Fact]
    public void Avif_irot_is_applied_and_bit_depth_is_reported()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var metadata = ImageDecoder.ReadMetadata(Asset("irot.avif"));
        Assert.Equal(12, metadata.RawWidth);
        Assert.Equal(34, metadata.RawHeight);
        Assert.Equal(34, metadata.DisplayWidth);
        Assert.Equal(12, metadata.DisplayHeight);
        Assert.Equal(ImageOrientation.LeftBottom, metadata.Orientation);
        Assert.Equal(10, metadata.BitDepth);

        var bitmap = ImageDecoder.DecodeFull(Asset("irot.avif"));
        Assert.Equal(34, bitmap.Width);
        Assert.Equal(12, bitmap.Height);
        Assert.True(bitmap.HasAlpha);

        using var raw = ImageDecoder.DecodeFullRaw(Asset("irot.avif"));
        Assert.Equal(12, raw.Width);
        Assert.Equal(34, raw.Height);
        Assert.Equal(34, raw.SourceWidth);
        Assert.Equal(12, raw.SourceHeight);
        Assert.Equal(ImageOrientation.LeftBottom, raw.Orientation);
        Assert.True(raw.HasAlpha);
        Assert.False(raw.IsDisposed);
    }

    [Fact]
    public void Avif_icc_and_exif_are_read()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var metadata = ImageDecoder.ReadMetadata(Asset("icc-exif.avif"));

        Assert.NotNull(metadata.IccDescription);
        Assert.Contains("sRGB", metadata.IccDescription, StringComparison.OrdinalIgnoreCase);
        Assert.False(metadata.IccAppliedToPixels);
        Assert.DoesNotContain("→", metadata.ColorSpaceDisplay);
        Assert.Equal("Google Pixel 4a", metadata.Exif.Camera);
        Assert.Equal(1, metadata.Exif.Orientation);
    }

    [Fact]
    public void Avif_animation_uses_the_first_frame()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var metadata = ImageDecoder.ReadMetadata(Asset("animated.avif"));
        Assert.Equal(150, metadata.DisplayWidth);
        Assert.Equal(150, metadata.DisplayHeight);
        Assert.Equal(1, metadata.FrameCount);
        Assert.False(metadata.IsAnimated);

        var bitmap = ImageDecoder.DecodeFull(Asset("animated.avif"));
        Assert.Equal(150, bitmap.Width);
        Assert.Equal(150, bitmap.Height);
    }

    [Fact]
    public void Avif_one_pixel_image_decodes()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var bitmap = ImageDecoder.DecodeFull(Asset("white-1x1.avif"));
        Assert.Equal(1, bitmap.Width);
        Assert.Equal(1, bitmap.Height);
        Assert.InRange(Pixel(bitmap, 0, 0).B, 245, 255);
    }

    [Fact]
    public void Avif_corrupt_file_reports_corrupt()
    {
        if (!SupportedFormats.SupportsAvif)
        {
            return;
        }

        var exception = Assert.Throws<ImageLoadException>(
            () => ImageDecoder.DecodeFull(Asset("corrupt.avif")));

        Assert.Equal(ImageLoadFailure.Corrupt, exception.Failure);
    }
}
