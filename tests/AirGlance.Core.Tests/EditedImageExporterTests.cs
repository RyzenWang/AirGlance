using AirGlance.Core.Imaging;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class EditedImageExporterTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "airglance-edited-" + Guid.NewGuid().ToString("N"));

    public EditedImageExporterTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public void Native_export_applies_crop_and_resize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg");
        var full = ImageDecoder.DecodeFull(source);
        var target = Path.Combine(_folder, "cropped.png");
        var options = new EditExportOptions(
            ExportFormat.Png,
            90,
            EditResizeMode.Original,
            0,
            KeepMetadata: true);

        EditedImageExporter.ExportNative(
            source,
            EditTransform.None,
            new CropRegion(0.25, 0.25, 0.5, 0.5),
            options,
            target);

        var output = ImageDecoder.DecodeFull(target);
        Assert.Equal(Math.Round(full.Width / 2.0), output.Width, 0);
        Assert.Equal(Math.Round(full.Height / 2.0), output.Height, 0);
    }

    [Fact]
    public void Native_export_does_not_overwrite_an_existing_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg");
        var target = Path.Combine(_folder, "existing.jpg");
        File.WriteAllText(target, "keep me");

        Assert.Throws<IOException>(() => EditedImageExporter.ExportNative(
            source,
            EditTransform.None,
            CropRegion.Full,
            new EditExportOptions(ExportFormat.Jpeg, 90, EditResizeMode.Original, 0, true),
            target));

        Assert.Equal("keep me", File.ReadAllText(target));
    }

    [Fact]
    public void Native_export_applies_the_same_mirror_then_rotate_order_as_the_viewer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg");
        var full = ImageDecoder.DecodeFull(source);
        var target = Path.Combine(_folder, "rotated.jpg");
        var transform = EditTransform.None.FlipHorizontal().RotateClockwise();

        EditedImageExporter.ExportNative(
            source,
            transform,
            CropRegion.Full,
            new EditExportOptions(ExportFormat.Jpeg, 90, EditResizeMode.Original, 0, true),
            target);

        var output = ImageDecoder.DecodeFull(target);
        Assert.Equal(full.Height, output.Width);
        Assert.Equal(full.Width, output.Height);
    }
}
