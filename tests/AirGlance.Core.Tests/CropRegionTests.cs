using AirGlance.Core.Imaging;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class CropRegionTests
{
    [Fact]
    public void Full_crop_is_recognized()
    {
        Assert.True(CropRegion.Full.IsFull);
        Assert.False(new CropRegion(0.1, 0.1, 0.8, 0.8).IsFull);
    }

    [Fact]
    public void Clamp_keeps_the_selection_inside_the_image()
    {
        var crop = new CropRegion(-0.2, 0.8, 1.4, 0.5).Clamp();

        Assert.Equal(0, crop.X);
        Assert.Equal(0.5, crop.Y);
        Assert.Equal(1, crop.Width);
        Assert.Equal(0.5, crop.Height);
    }

    [Fact]
    public void ToPixels_uses_the_transformed_image_dimensions()
    {
        var crop = new CropRegion(0.25, 0.25, 0.5, 0.5);
        var (x, y, width, height) = crop.ToPixels(1600, 900);

        Assert.Equal((400, 225, 800, 450), (x, y, width, height));
    }

    [Fact]
    public void ToPixels_keeps_at_least_one_pixel()
    {
        var (_, _, width, height) = new CropRegion(0.999, 0.999, 0.0001, 0.0001)
            .ToPixels(10, 10);

        Assert.True(width >= 1);
        Assert.True(height >= 1);
    }

    [Fact]
    public void Target_size_never_upscales()
    {
        var options = new EditExportOptions(
            ExportFormat.Jpeg,
            90,
            EditResizeMode.LongEdge,
            8000,
            KeepMetadata: true);

        Assert.Equal((2400, 1350), options.TargetSize(2400, 1350));
    }

    [Theory]
    [InlineData(EditResizeMode.LongEdge, 1200, 1200, 900)]
    [InlineData(EditResizeMode.FitWithin, 1200, 1200, 900)]
    public void Target_size_respects_the_resize_mode(
        EditResizeMode mode,
        int value,
        int width,
        int height)
    {
        var options = new EditExportOptions(
            ExportFormat.Jpeg,
            90,
            mode,
            value,
            KeepMetadata: true);

        Assert.Equal((width, height), options.TargetSize(1600, 1200));
    }

    [Fact]
    public void Png_quality_is_still_clamped_but_unused_by_the_encoder()
    {
        var options = new EditExportOptions(
            ExportFormat.Png,
            5,
            EditResizeMode.Original,
            0,
            KeepMetadata: false);

        Assert.Equal(40, options.NormalizedQuality);
    }
}
