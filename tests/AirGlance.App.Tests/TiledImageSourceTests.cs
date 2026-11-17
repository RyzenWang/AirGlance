using AirGlance.App.Imaging;
using AirGlance.Core.Imaging;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class TiledImageSourceTests
{
    [AvaloniaFact]
    public void Tiled_source_preserves_orientation_edits_and_pixels()
    {
        EditTransform[] edits =
        [
            EditTransform.None,
            EditTransform.None.RotateClockwise(),
            EditTransform.None.RotateCounterClockwise(),
            EditTransform.None.FlipHorizontal(),
            EditTransform.None.FlipVertical()
        ];

        for (var orientation = 1; orientation <= 8; orientation++)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                $"orientation-{orientation}.jpg");
            Assert.True(File.Exists(path), path);

            var source = ImageDecoder.DecodeFull(path);
            using var raw = ImageDecoder.DecodeFullRaw(path);
            foreach (var edit in edits)
            {
                var expected = ImageTransform.Apply(source, edit);
                using var tiles = TiledImageSource.Create(raw, edit);
                Assert.Equal(expected.Width, tiles.PixelWidth);
                Assert.Equal(expected.Height, tiles.PixelHeight);

                for (var row = 0; row < tiles.TileRows; row++)
                {
                    for (var column = 0; column < tiles.TileColumns; column++)
                    {
                        var tileBytes = tiles.ReadTileBytesForTest(column, row);
                        var tileWidth = Math.Min(
                            TiledImageSource.TileSize,
                            expected.Width - column * TiledImageSource.TileSize);
                        var tileHeight = Math.Min(
                            TiledImageSource.TileSize,
                            expected.Height - row * TiledImageSource.TileSize);
                        for (var y = 0; y < tileHeight; y++)
                        {
                            for (var x = 0; x < tileWidth; x++)
                            {
                                var actual = BitConverter.ToUInt32(
                                    tileBytes,
                                    (y * TiledImageSource.TileSize + x) * 4);
                                var imageX = column * TiledImageSource.TileSize + x;
                                var imageY = row * TiledImageSource.TileSize + y;
                                var expectedPixel = BitConverter.ToUInt32(
                                    expected.Pixels,
                                    (imageY * expected.Width + imageX) * 4);
                                if (expectedPixel != actual)
                                {
                                    Assert.Fail(
                                        $"mismatch o={orientation} edit={edit} " +
                                        $"col={column} row={row} x={x} y={y} " +
                                        $"expected=0x{expectedPixel:X8} actual=0x{actual:X8}");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
