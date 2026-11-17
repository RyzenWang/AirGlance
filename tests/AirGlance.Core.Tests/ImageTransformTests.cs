using System.Runtime.InteropServices;
using AirGlance.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class ImageTransformTests
{
    private static readonly uint[] Pixels2x3 = [0xFF000001, 0xFF000002, 0xFF000003, 0xFF000004, 0xFF000005, 0xFF000006];

    private static DecodedBitmap Bitmap2x3() =>
        new(2, 3, MemoryMarshal.AsBytes(Pixels2x3.AsSpan()).ToArray(), 2, 3, 1.0);

    private static uint[] Read(DecodedBitmap bitmap) =>
        MemoryMarshal.Cast<byte, uint>(bitmap.Pixels.AsSpan(0, bitmap.Width * bitmap.Height * 4))
            .ToArray()
            .Select(value => value & 0xFFFFFF)
            .ToArray();

    [Theory]
    [InlineData(ImageOrientation.TopLeft, new uint[] { 1, 2, 3, 4, 5, 6 }, 2, 3)]
    [InlineData(ImageOrientation.TopRight, new uint[] { 2, 1, 4, 3, 6, 5 }, 2, 3)]
    [InlineData(ImageOrientation.BottomRight, new uint[] { 6, 5, 4, 3, 2, 1 }, 2, 3)]
    [InlineData(ImageOrientation.BottomLeft, new uint[] { 5, 6, 3, 4, 1, 2 }, 2, 3)]
    [InlineData(ImageOrientation.LeftTop, new uint[] { 1, 3, 5, 2, 4, 6 }, 3, 2)]
    [InlineData(ImageOrientation.RightTop, new uint[] { 5, 3, 1, 6, 4, 2 }, 3, 2)]
    [InlineData(ImageOrientation.RightBottom, new uint[] { 6, 4, 2, 5, 3, 1 }, 3, 2)]
    [InlineData(ImageOrientation.LeftBottom, new uint[] { 2, 4, 6, 1, 3, 5 }, 3, 2)]
    public void Exif_orientation_moves_pixels_to_the_right_place(
        ImageOrientation orientation,
        uint[] expected,
        int expectedWidth,
        int expectedHeight)
    {
        var oriented = ImageTransform.ApplyOrientation(Bitmap2x3(), orientation, 2, 3);

        Assert.Equal(expectedWidth, oriented.Width);
        Assert.Equal(expectedHeight, oriented.Height);
        Assert.Equal(expected, Read(oriented).Select(value => value & 0xFFFFFF));
    }

    [Fact]
    public void Rotating_clockwise_twice_is_a_half_turn()
    {
        var bitmap = Bitmap2x3();
        var once = ImageTransform.RotateClockwise(bitmap);
        var twice = ImageTransform.RotateClockwise(once);

        Assert.Equal(2, twice.Width);
        Assert.Equal(3, twice.Height);
        Assert.Equal(new uint[] { 6, 5, 4, 3, 2, 1 }, Read(twice));
    }

    [Fact]
    public void Horizontal_and_vertical_flips_equal_a_half_turn()
    {
        var transform = EditTransform.None.FlipHorizontal().FlipVertical();
        var result = ImageTransform.Apply(Bitmap2x3(), transform);

        Assert.Equal(new uint[] { 6, 5, 4, 3, 2, 1 }, Read(result));
    }

    [Fact]
    public void Four_clockwise_rotations_return_the_original()
    {
        var result = ImageTransform.Apply(
            Bitmap2x3(),
            EditTransform.None
                .RotateClockwise()
                .RotateClockwise()
                .RotateClockwise()
                .RotateClockwise());

        Assert.Equal(new uint[] { 1, 2, 3, 4, 5, 6 }, Read(result));
    }

    [Fact]
    public void Composed_transform_matches_applying_edits_one_by_one()
    {
        var transforms = new[]
        {
            EditTransform.None.RotateClockwise().FlipHorizontal(),
            EditTransform.None.FlipVertical().RotateCounterClockwise(),
            EditTransform.None.RotateClockwise().RotateClockwise().FlipHorizontal().RotateCounterClockwise(),
            EditTransform.None.FlipHorizontal().RotateCounterClockwise().FlipVertical().RotateClockwise()
        };

        foreach (var transform in transforms)
        {
            var composed = ImageTransform.Apply(Bitmap2x3(), transform);

            var stepwise = Bitmap2x3();
            foreach (var step in transform.RotateClockwiseSteps())
            {
                stepwise = ImageTransform.Apply(stepwise, step);
            }

            Assert.Equal(Read(stepwise), Read(composed));
            Assert.Equal(stepwise.Width, composed.Width);
            Assert.Equal(stepwise.Height, composed.Height);
        }
    }

    [Fact]
    public void Scale_and_source_size_survive_rotation()
    {
        var bitmap = new DecodedBitmap(100, 50, new byte[100 * 50 * 4], 1000, 500, 0.1);

        var rotated = ImageTransform.Apply(bitmap, EditTransform.None.RotateClockwise());

        Assert.Equal(50, rotated.Width);
        Assert.Equal(100, rotated.Height);
        Assert.Equal(500, rotated.SourceWidth);
        Assert.Equal(1000, rotated.SourceHeight);
        Assert.Equal(0.1, rotated.Scale, 6);
    }
}

internal static class EditTransformTestExtensions
{
    /// <summary>Expands a canonical transform into the keystrokes that produce it.</summary>
    public static IEnumerable<EditTransform> RotateClockwiseSteps(this EditTransform transform)
    {
        if (transform.MirrorHorizontal)
        {
            yield return EditTransform.None.FlipHorizontal();
        }

        for (var index = 0; index < transform.QuarterTurns; index++)
        {
            yield return EditTransform.None.RotateClockwise();
        }
    }
}
