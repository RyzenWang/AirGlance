using System.Security.Cryptography;
using AirGlance.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class ImageExporterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "airglance-export-" + Guid.NewGuid().ToString("N"));

    public ImageExporterTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private static SKBitmap DecodeBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return SKBitmap.Decode(stream);
    }

    [Theory]
    [InlineData(ExportFormat.Jpeg, new byte[] { 0xFF, 0xD8 })]
    [InlineData(ExportFormat.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(ExportFormat.WebP, new byte[] { 0x52, 0x49, 0x46, 0x46 })]
    public void Encodes_each_supported_format(ExportFormat format, byte[] signature)
    {
        var image = ImageDecoder.DecodeFull(Asset("orientation-1.jpg"));

        var bytes = ImageExporter.Encode(image, format);

        Assert.True(bytes.Length > signature.Length);
        Assert.Equal(signature, bytes.Take(signature.Length));
        using var decoded = DecodeBytes(bytes);
        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
    }

    [Fact]
    public void Jpeg_flattens_transparency_to_white()
    {
        var image = ImageDecoder.DecodeFull(Asset("alpha.png"));

        var bytes = ImageExporter.Encode(image, ExportFormat.Jpeg);
        using var decoded = DecodeBytes(bytes);
        var pixel = decoded.GetPixel(0, 0);

        Assert.True(pixel.Red > 230 && pixel.Green > 230 && pixel.Blue > 230);
    }

    [Fact]
    public void Png_keeps_transparency()
    {
        var image = ImageDecoder.DecodeFull(Asset("alpha.png"));

        var bytes = ImageExporter.Encode(image, ExportFormat.Png);
        using var decoded = DecodeBytes(bytes);

        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Edited_encoder_crops_and_resizes_in_the_fallback_path()
    {
        var image = ImageDecoder.DecodeFull(Asset("orientation-1.jpg"));
        var options = new EditExportOptions(
            ExportFormat.Png,
            90,
            EditResizeMode.LongEdge,
            32,
            KeepMetadata: false);

        var bytes = ImageExporter.EncodeEdited(
            image,
            new CropRegion(0.25, 0.25, 0.5, 0.5),
            options);
        using var decoded = DecodeBytes(bytes);

        Assert.Equal(32, Math.Max(decoded.Width, decoded.Height));
        Assert.Equal(
            Math.Round(image.Width / 2.0 / (image.Height / 2.0) * decoded.Height),
            decoded.Width,
            0);
    }

    [Fact]
    public void Save_copy_uses_a_new_file_and_never_overwrites()
    {
        var target = Path.Combine(_folder, "photo-copy.png");
        File.WriteAllText(target, "keep me");
        var bytes = ImageExporter.Encode(ImageDecoder.DecodeFull(Asset("orientation-1.jpg")), ExportFormat.Png);

        var written = ImageExporter.WriteNewFile(target, bytes);

        Assert.Equal(Path.Combine(_folder, "photo-copy-2.png"), written);
        Assert.Equal("keep me", File.ReadAllText(target));
        Assert.True(File.Exists(written));
    }

    [Fact]
    public void Default_copy_name_avoids_existing_files()
    {
        var source = Path.Combine(_folder, "photo.jpg");
        File.WriteAllBytes(source, [0]);

        var first = ImageExporter.GetDefaultCopyPath(source, ExportFormat.Jpeg);
        Assert.Equal(Path.Combine(_folder, "photo-copy.jpg"), first);

        File.WriteAllText(first, "x");
        var second = ImageExporter.GetDefaultCopyPath(source, ExportFormat.Jpeg);
        Assert.Equal(Path.Combine(_folder, "photo-copy-2.jpg"), second);
    }

    [Fact]
    public void Rotating_and_saving_leaves_the_original_bytes_untouched()
    {
        var source = Path.Combine(_folder, "original.jpg");
        File.Copy(Asset("exif-full.jpg"), source);
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var lengthBefore = new FileInfo(source).Length;

        var image = ImageDecoder.DecodeFull(source);
        var rotated = ImageTransform.Apply(image, EditTransform.None.RotateClockwise());
        var bytes = ImageExporter.Encode(rotated, ExportFormat.Jpeg);
        ImageExporter.WriteNewFile(Path.Combine(_folder, "original-copy.jpg"), bytes);

        var after = SHA256.HashData(File.ReadAllBytes(source));
        Assert.Equal(before, after);
        Assert.Equal(lengthBefore, new FileInfo(source).Length);
    }

    [Fact]
    public void Encoded_image_is_rotated()
    {
        var image = ImageDecoder.DecodeFull(Asset("orientation-1.jpg"));
        var rotated = ImageTransform.Apply(image, EditTransform.None.RotateClockwise());
        var bytes = ImageExporter.Encode(rotated, ExportFormat.Png);
        using var decoded = DecodeBytes(bytes);

        Assert.Equal(40, decoded.Width);
        Assert.Equal(80, decoded.Height);
    }
}
