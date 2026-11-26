using AirGlance.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace AirGlance.Core.Tests;

/// <summary>
/// Proves the SkiaSharp scanline API the streamed tile writer depends on: a codec can be
/// asked for a few rows at a time, in BGRA, without materialising the whole image. If a
/// future SkiaSharp drops or changes it, this test fails before the tile path does.
/// </summary>
public sealed class ScanlineDecodeProbeTests
{
    [Fact]
    public void Jpeg_decodes_row_by_row_without_a_full_buffer()
    {
        var path = Asset("orientation-1.jpg");
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);

        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());

        var started = codec.StartScanlineDecode(info);
        Assert.Equal(SKCodecResult.Success, started);

        var rowBytes = info.Width * 4;
        const int batch = 8;
        var buffer = GC.AllocateUninitializedArray<byte>(rowBytes * batch);
        var rows = 0;
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var pointer = handle.AddrOfPinnedObject();
            while (true)
            {
                var lines = codec.GetScanlines(pointer, batch, rowBytes);
                if (lines <= 0)
                {
                    break;
                }

                rows += lines;
            }
        }
        finally
        {
            handle.Free();
        }

        Assert.Equal(info.Height, rows);
        Assert.NotEqual((byte)0, buffer[0] | buffer[1] | buffer[2] | buffer[3]);
    }

    [Fact]
    public void Png_reports_scanline_decode_as_unimplemented()
    {
        // The streamed tile writer only accepts formats that implement scanline decode.
        // SkiaSharp 4.150.1 returns Unimplemented for PNG, which is why a large PNG still
        // takes the full-raster path; this test records that instead of assuming it.
        var path = Asset("orientation-6.png");
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);

        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());

        Assert.Equal(SKCodecResult.Unimplemented, codec.StartScanlineDecode(info));
    }

    private static string Asset(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", name);
}
