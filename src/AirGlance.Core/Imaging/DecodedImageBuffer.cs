using System.Runtime.InteropServices;

namespace AirGlance.Core.Imaging;

/// <summary>
/// A full-resolution decode held in unmanaged memory. The tile writer consumes it and
/// frees it immediately, so a 100 MP decode does not leave a 400 MB managed array (and
/// its committed LOH segment) alive for the lifetime of the process.
/// </summary>
public sealed class DecodedImageBuffer : IDisposable
{
    internal DecodedImageBuffer(
        IntPtr pixels,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight,
        ImageOrientation orientation,
        bool hasAlpha)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Orientation = orientation;
        HasAlpha = hasAlpha;
    }

    public IntPtr Pixels { get; private set; }

    /// <summary>Raw decoded width before EXIF orientation.</summary>
    public int Width { get; }

    /// <summary>Raw decoded height before EXIF orientation.</summary>
    public int Height { get; }

    /// <summary>Width after EXIF orientation.</summary>
    public int SourceWidth { get; }

    /// <summary>Height after EXIF orientation.</summary>
    public int SourceHeight { get; }

    public ImageOrientation Orientation { get; }

    public bool HasAlpha { get; }

    public long ByteLength => (long)Width * Height * 4;

    public bool IsDisposed => Pixels == IntPtr.Zero;

    public void Dispose()
    {
        if (Pixels == IntPtr.Zero)
        {
            return;
        }

        unsafe
        {
            NativeMemory.Free((void*)Pixels);
        }

        Pixels = IntPtr.Zero;
    }
}
