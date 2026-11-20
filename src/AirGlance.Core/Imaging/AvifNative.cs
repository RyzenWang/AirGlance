using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AirGlance.Core.Imaging;

internal sealed class AvifNativeException : Exception
{
    public AvifNativeException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct AvifInfo
{
    public uint Width;
    public uint Height;
    public uint Depth;
    public uint TransformFlags;
    public uint IrotAngle;
    public uint ImirAxis;
    public uint ClapX;
    public uint ClapY;
    public uint ClapWidth;
    public uint ClapHeight;
    public uint FrameCount;
    public int HasAlpha;
    public nuint IccSize;
    public nuint ExifSize;
}

/// <summary>
/// The small C ABI over libavif + dav1d. The native library is built by
/// native/avif/build.ps1 and shipped next to the executable.
/// </summary>
internal static unsafe partial class AvifNative
{
    private const string LibraryName = "airglance_avif";
    private const int ErrorCapacity = 512;

    public static bool IsAvailable { get; } = Probe();

    public static string Version =>
        IsAvailable ? Marshal.PtrToStringUTF8(VersionCore()) ?? "unknown" : "unavailable";

    [LibraryImport(LibraryName, EntryPoint = "airglance_avif_probe")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ProbeCore();

    [LibraryImport(LibraryName, EntryPoint = "airglance_avif_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr VersionCore();

    [LibraryImport(LibraryName, EntryPoint = "airglance_avif_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int InfoCore(
        byte* data,
        nuint size,
        out AvifInfo info,
        byte* icc,
        nuint iccCapacity,
        byte* exif,
        nuint exifCapacity,
        byte* error,
        nuint errorCapacity);

    [LibraryImport(LibraryName, EntryPoint = "airglance_avif_decode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int DecodeCore(
        byte* data,
        nuint size,
        byte* destination,
        nuint destinationCapacity,
        out uint width,
        out uint height,
        out int hasAlpha,
        byte* error,
        nuint errorCapacity);

    public static AvifInfo ReadInfo(ReadOnlySpan<byte> data, Span<byte> icc, Span<byte> exif)
    {
        fixed (byte* dataPointer = data)
        fixed (byte* iccPointer = icc)
        fixed (byte* exifPointer = exif)
        {
            byte* error = stackalloc byte[ErrorCapacity];
            var result = InfoCore(
                dataPointer,
                (nuint)data.Length,
                out var info,
                iccPointer,
                (nuint)icc.Length,
                exifPointer,
                (nuint)exif.Length,
                error,
                ErrorCapacity);

            if (result != 0)
            {
                throw CreateException(result, error);
            }

            return info;
        }
    }

    public static (uint Width, uint Height, int HasAlpha) Decode(
        ReadOnlySpan<byte> data,
        byte* destination,
        int destinationCapacity)
    {
        fixed (byte* dataPointer = data)
        {
            byte* error = stackalloc byte[ErrorCapacity];
            var result = DecodeCore(
                dataPointer,
                (nuint)data.Length,
                destination,
                (nuint)destinationCapacity,
                out var width,
                out var height,
                out var hasAlpha,
                error,
                ErrorCapacity);

            if (result != 0)
            {
                throw CreateException(result, error);
            }

            return (width, height, hasAlpha);
        }
    }

    private static bool Probe()
    {
        try
        {
            return ProbeCore() != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static AvifNativeException CreateException(int code, byte* error)
    {
        var message = Marshal.PtrToStringUTF8((IntPtr)error);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = code switch
            {
                1 => "The AVIF decoder rejected the request.",
                2 => "The AVIF decoder ran out of memory.",
                4 => "The AVIF destination buffer is too small.",
                _ => "The AVIF could not be decoded."
            };
        }

        return new AvifNativeException(code, message);
    }
}
