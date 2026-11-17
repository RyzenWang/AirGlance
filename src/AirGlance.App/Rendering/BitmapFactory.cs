using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AirGlance.App.Rendering;

/// <summary>
/// Copies decoded BGRA pixels into an Avalonia bitmap. The decoded pixels and the
/// bitmap use the same layout, so this is one bulk copy, not a PNG round trip.
/// </summary>
internal static class BitmapFactory
{
    public static WriteableBitmap FromDecoded(DecodedBitmap image)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var framebuffer = bitmap.Lock();
        unsafe
        {
            var destination = (byte*)framebuffer.Address;
            fixed (byte* source = image.Pixels)
            {
                if (framebuffer.RowBytes == image.Stride)
                {
                    Buffer.MemoryCopy(
                        source,
                        destination,
                        (long)framebuffer.RowBytes * image.Height,
                        (long)image.Stride * image.Height);
                }
                else
                {
                    for (var y = 0; y < image.Height; y++)
                    {
                        Buffer.MemoryCopy(
                            source + y * image.Stride,
                            destination + y * framebuffer.RowBytes,
                            framebuffer.RowBytes,
                            Math.Min(image.Stride, framebuffer.RowBytes));
                    }
                }
            }
        }

        return bitmap;
    }
}
