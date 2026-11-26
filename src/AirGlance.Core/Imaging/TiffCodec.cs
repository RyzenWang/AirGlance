using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Threading;
using BitMiracle.LibTiff.Classic;

namespace AirGlance.Core.Imaging;

/// <summary>
/// The TIFF helpers the preview decoder and the region reader both need: byte readers,
/// tag helpers and the sample-to-BGRA conversion. They used to be copied into both
/// classes, where they had already started to drift apart; the format-detection ladders
/// and the pixel switch still live in each class and are the next extraction step.
/// </summary>
internal static class TiffCodec
{
    internal static ushort ReadUInt16(Stream stream, bool little)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactly(stream, buffer);
        return little
            ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    internal static uint ReadUInt32(Stream stream, bool little)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(stream, buffer);
        return little
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    internal static ulong ReadUInt64(Stream stream, bool little)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactly(stream, buffer);
        return little
            ? BinaryPrimitives.ReadUInt64LittleEndian(buffer)
            : BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    internal static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count <= 0)
            {
                throw new EndOfStreamException();
            }

            read += count;
        }
    }

    internal static uint Opaque(byte red, byte green, byte blue) =>
        0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;

    internal static uint PremultipliedBgra(byte red, byte green, byte blue, byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        if (alpha != 255)
        {
            red = (byte)((red * alpha + 127) / 255);
            green = (byte)((green * alpha + 127) / 255);
            blue = (byte)((blue * alpha + 127) / 255);
        }

        return ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
    }

    internal static void WriteBgra(byte[] pixels, int offset, uint value)
    {
        // One 32-bit write per pixel keeps the parallel tile/strip workers from
        // tearing a pixel into mixed channels. The last writer wins, which is
        // fine for a proxy preview.
        Volatile.Write(ref Unsafe.As<byte, uint>(ref pixels[offset]), value);
    }

    internal static float ReadSingleNative(byte[] source, int offset)
    {
        var bits = BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4))
            : BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(bits);
    }

    internal static byte FromFloat(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    internal static float ReadHalfNative(byte[] source, int offset) =>
        (float)BitConverter.UInt16BitsToHalf(ReadUInt16Native(source, offset));

    internal static byte Signed16ToByte(ushort value) =>
        (byte)Math.Clamp((unchecked((short)value) + 32768) >> 8, 0, 255);

    internal static ushort ReadUInt16Native(byte[] row, int offset)
    {
        // LibTiff returns 16-bit scanline samples in the machine byte order.
        return BitConverter.IsLittleEndian
            ? (ushort)(row[offset] | (row[offset + 1] << 8))
            : (ushort)((row[offset] << 8) | row[offset + 1]);
    }

    internal static (int X, int Y) ReadSubsampling(Tiff image)
    {
        var field = image.GetField(TiffTag.YCBCRSUBSAMPLING);
        return field is { Length: >= 2 }
            ? (field[0].ToInt(), field[1].ToInt())
            : (0, 0);
    }

    internal static bool HasUnitSubsampling(Tiff image)
    {
        var (x, y) = ReadSubsampling(image);
        return x == 1 && y == 1;
    }

    internal static uint FromYCbCr(byte y, byte cb, byte cr)
    {
        var blueDifference = cb - 128;
        var redDifference = cr - 128;
        var red = ClampByte(y + ((91881 * redDifference) >> 16));
        var green = ClampByte(y - ((22554 * blueDifference + 46802 * redDifference) >> 16));
        var blue = ClampByte(y + ((116130 * blueDifference) >> 16));
        return Opaque((byte)red, (byte)green, (byte)blue);
    }

    internal static int ClampByte(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    internal static void EnableJpegRgbMode(Tiff image)
    {
        if (TagInt(image, TiffTag.COMPRESSION) == 7 &&
            TagInt(image, TiffTag.PHOTOMETRIC) == 6 &&
            TagInt(image, TiffTag.SAMPLESPERPIXEL) == 3)
        {
            image.SetField(TiffTag.JPEGCOLORMODE, JpegColorMode.RGB);
        }
    }

    internal static ushort[]? ReadColorMap(Tiff image)
    {
        var field = image.GetField(TiffTag.COLORMAP);
        if (field is null)
        {
            return null;
        }

        var parts = new List<ushort[]>();
        foreach (var value in field)
        {
            switch (value.Value)
            {
                case ushort[] bytes:
                    parts.Add(bytes);
                    break;
                case short[] shorts:
                {
                    var map = new ushort[shorts.Length];
                    for (var index = 0; index < shorts.Length; index++)
                    {
                        map[index] = unchecked((ushort)shorts[index]);
                    }

                    parts.Add(map);
                    break;
                }
                case int[] ints:
                {
                    var map = new ushort[ints.Length];
                    for (var index = 0; index < ints.Length; index++)
                    {
                        map[index] = (ushort)Math.Clamp(ints[index], 0, ushort.MaxValue);
                    }

                    parts.Add(map);
                    break;
                }
            }
        }

        if (parts.Count >= 3)
        {
            var length = Math.Min(parts[0].Length, Math.Min(parts[1].Length, parts[2].Length));
            var combined = new ushort[3 * length];
            for (var channel = 0; channel < 3; channel++)
            {
                Array.Copy(parts[channel], 0, combined, channel * length, length);
            }

            return combined;
        }

        return parts.Count == 1 ? parts[0] : null;
    }

    internal static int TagInt(Tiff image, TiffTag tag)
    {
        var value = TagLong(image, tag);
        return value is <= 0 or > int.MaxValue ? 0 : (int)value;
    }

    internal static long TagLong(Tiff image, TiffTag tag)
    {
        var field = image.GetField(tag);
        return field is { Length: > 0 } ? field[0].ToLong() : 0;
    }

    internal static ushort ReadU16(ReadOnlySpan<byte> source, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt16LittleEndian(source)
            : BinaryPrimitives.ReadUInt16BigEndian(source);

    internal static uint ReadU32(ReadOnlySpan<byte> source, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);

    internal static ulong ReadU64(ReadOnlySpan<byte> source, bool little) =>
        little
            ? BinaryPrimitives.ReadUInt64LittleEndian(source)
            : BinaryPrimitives.ReadUInt64BigEndian(source);
    // The region reader spelled its stream readers ReadU16/32/64 and the preview decoder
    // spelled them ReadUInt16/32/64. Both are kept as one implementation plus a thin
    // alias so neither caller had to change its call sites during the extraction.
    internal static ushort ReadU16(Stream stream, bool little) => ReadUInt16(stream, little);

    internal static uint ReadU32(Stream stream, bool little) => ReadUInt32(stream, little);

    internal static ulong ReadU64(Stream stream, bool little) => ReadUInt64(stream, little);



    internal static uint ReadRawPixel(byte[] source, int offset, in TiffRawFormat format)
    {
        switch (format.Layout)
        {
            case TiffRawLayout.Rgb:
                return Opaque(source[offset], source[offset + 1], source[offset + 2]);
            case TiffRawLayout.Rgb16:
                return Opaque(
                    (byte)(ReadUInt16Native(source, offset) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 2) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 4) >> 8));
            case TiffRawLayout.Cmyk8:
                var cyan = source[offset];
                var magenta = source[offset + 1];
                var yellow = source[offset + 2];
                var black = source[offset + 3];
                return Opaque(
                    (byte)(255 - Math.Min(255, cyan + black)),
                    (byte)(255 - Math.Min(255, magenta + black)),
                    (byte)(255 - Math.Min(255, yellow + black)));
            case TiffRawLayout.Rgba8:
                return PremultipliedBgra(
                    source[offset],
                    source[offset + 1],
                    source[offset + 2],
                    source[offset + 3]);
            case TiffRawLayout.Rgba16:
                return PremultipliedBgra(
                    (byte)(ReadUInt16Native(source, offset) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 2) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 4) >> 8),
                    (byte)(ReadUInt16Native(source, offset + 6) >> 8));
            case TiffRawLayout.Float32Gray:
                var grayFloat = FromFloat(ReadSingleNative(source, offset));
                return Opaque(grayFloat, grayFloat, grayFloat);
            case TiffRawLayout.Float32GrayInverted:
                var invertedFloat = (byte)(255 - FromFloat(ReadSingleNative(source, offset)));
                return Opaque(invertedFloat, invertedFloat, invertedFloat);
            case TiffRawLayout.Float32Rgb:
                return Opaque(
                    FromFloat(ReadSingleNative(source, offset)),
                    FromFloat(ReadSingleNative(source, offset + 4)),
                    FromFloat(ReadSingleNative(source, offset + 8)));
            case TiffRawLayout.Float16Gray:
                var grayHalf = FromFloat(ReadHalfNative(source, offset));
                return Opaque(grayHalf, grayHalf, grayHalf);
            case TiffRawLayout.Float16GrayInverted:
                var invertedHalf = (byte)(255 - FromFloat(ReadHalfNative(source, offset)));
                return Opaque(invertedHalf, invertedHalf, invertedHalf);
            case TiffRawLayout.Float16Rgb:
                return Opaque(
                    FromFloat(ReadHalfNative(source, offset)),
                    FromFloat(ReadHalfNative(source, offset + 2)),
                    FromFloat(ReadHalfNative(source, offset + 4)));
            case TiffRawLayout.Gray16Int:
                var graySigned = Signed16ToByte(ReadUInt16Native(source, offset));
                return Opaque(graySigned, graySigned, graySigned);
            case TiffRawLayout.Rgb16Int:
                return Opaque(
                    Signed16ToByte(ReadUInt16Native(source, offset)),
                    Signed16ToByte(ReadUInt16Native(source, offset + 2)),
                    Signed16ToByte(ReadUInt16Native(source, offset + 4)));
            case TiffRawLayout.Palette8:
                var palette = format.Palette!;
                var index = source[offset];
                return Opaque(
                    (byte)(palette[index] >> 8),
                    (byte)(palette[256 + index] >> 8),
                    (byte)(palette[512 + index] >> 8));
            case TiffRawLayout.Palette16:
                var palette16 = format.Palette!;
                var index16 = ReadUInt16Native(source, offset);
                return Opaque(
                    (byte)(palette16[index16] >> 8),
                    (byte)(palette16[65536 + index16] >> 8),
                    (byte)(palette16[131072 + index16] >> 8));
            case TiffRawLayout.YCbCr8:
                return FromYCbCr(source[offset], source[offset + 1], source[offset + 2]);
            case TiffRawLayout.Gray:
                return Opaque(source[offset], source[offset], source[offset]);
            case TiffRawLayout.Gray16:
                var gray16 = (byte)(ReadUInt16Native(source, offset) >> 8);
                return Opaque(gray16, gray16, gray16);
            case TiffRawLayout.GrayInverted:
                var inverted = (byte)(255 - source[offset]);
                return Opaque(inverted, inverted, inverted);
            case TiffRawLayout.Gray16Inverted:
                var inverted16 = (byte)(255 - (ReadUInt16Native(source, offset) >> 8));
                return Opaque(inverted16, inverted16, inverted16);
            default:
                return 0xFF000000u;
        }
    }
internal static int RawPixelBytes(TiffRawLayout layout) =>
    layout switch
    {
        TiffRawLayout.None => 0,
        TiffRawLayout.YCbCrSub8 => 0,
        TiffRawLayout.Rgb or TiffRawLayout.RgbPlanar8 or TiffRawLayout.YCbCr8 => 3,
        TiffRawLayout.Rgb16 or TiffRawLayout.RgbPlanar16 => 6,
        TiffRawLayout.Cmyk8 or TiffRawLayout.Rgba8 or TiffRawLayout.CmykPlanar8 or TiffRawLayout.RgbaPlanar8 => 4,
        TiffRawLayout.CmykPlanar16 => 8,
        TiffRawLayout.Rgba16 => 8,
        TiffRawLayout.Float32Gray or TiffRawLayout.Float32GrayInverted => 4,
        TiffRawLayout.Float32Rgb => 12,
        TiffRawLayout.Float16Gray or TiffRawLayout.Float16GrayInverted => 2,
        TiffRawLayout.Float16Rgb => 6,
        TiffRawLayout.Gray16Int => 2,
        TiffRawLayout.Rgb16Int => 6,
        TiffRawLayout.Palette16 => 2,
        TiffRawLayout.Gray or TiffRawLayout.GrayInverted or TiffRawLayout.Palette8 => 1,
        _ => 2
    };

    internal static TiffRawFormat GetRawFormat(Tiff image)
    {
        var bits = TagInt(image, TiffTag.BITSPERSAMPLE);
        if (bits is not (8 or 16 or 32))
        {
            return default;
        }

        var samples = TagInt(image, TiffTag.SAMPLESPERPIXEL);
        if (samples == 0)
        {
            samples = 1;
        }

        var planar = TagInt(image, TiffTag.PLANARCONFIG);
        if (planar == 2)
        {
            var planarCompression = TagInt(image, TiffTag.COMPRESSION);
            var planarPhoto = TagInt(image, TiffTag.PHOTOMETRIC);
            if (planarCompression is 1 or 5 or 8 or 32946 or 32773)
            {
                if (bits == 8 && samples == 3 && planarPhoto == 2)
                {
                    return new TiffRawFormat(TiffRawLayout.RgbPlanar8, null);
                }

                if (bits == 16 && samples == 3 && planarPhoto == 2)
                {
                    return new TiffRawFormat(TiffRawLayout.RgbPlanar16, null);
                }

                if (bits == 8 && samples == 4 && planarPhoto == 5)
                {
                    return new TiffRawFormat(TiffRawLayout.CmykPlanar8, null);
                }

                if (bits == 16 && samples == 4 && planarPhoto == 5)
                {
                    return new TiffRawFormat(TiffRawLayout.CmykPlanar16, null);
                }

                if (bits == 8 && samples == 4 && planarPhoto == 2)
                {
                    return new TiffRawFormat(TiffRawLayout.RgbaPlanar8, null);
                }

                if (bits == 16 && samples == 4 && planarPhoto == 2)
                {
                    return new TiffRawFormat(TiffRawLayout.RgbaPlanar16, null);
                }
            }

            return default;
        }

        if (planar is not (0 or 1))
        {
            return default;
        }

        var compression = TagInt(image, TiffTag.COMPRESSION);
        if (compression is not (1 or 5 or 7 or 8 or 32946 or 32773))
        {
            return default;
        }

        if (bits == 16)
        {
            var sampleFormat = TagInt(image, TiffTag.SAMPLEFORMAT);
            if (sampleFormat is not (0 or 1 or 2 or 3))
            {
                return default;
            }
        }

        var photometric = TagInt(image, TiffTag.PHOTOMETRIC);
        if (bits == 32 && TagInt(image, TiffTag.SAMPLEFORMAT) == 3 && compression != 7)
        {
            if (samples == 3 && photometric == 2)
            {
                return new TiffRawFormat(TiffRawLayout.Float32Rgb, null);
            }

            if (samples == 1 && photometric == 1)
            {
                return new TiffRawFormat(TiffRawLayout.Float32Gray, null);
            }

            if (samples == 1 && photometric == 0)
            {
                return new TiffRawFormat(TiffRawLayout.Float32GrayInverted, null);
            }
        }

        if (bits == 16 && TagInt(image, TiffTag.SAMPLEFORMAT) == 2 && compression != 7)
        {
            if (samples == 3 && photometric == 2)
            {
                return new TiffRawFormat(TiffRawLayout.Rgb16Int, null);
            }

            if (samples == 1 && photometric == 1)
            {
                return new TiffRawFormat(TiffRawLayout.Gray16Int, null);
            }

            return default;
        }

        if (bits == 16 && TagInt(image, TiffTag.SAMPLEFORMAT) == 3 && compression != 7)
        {
            if (samples == 3 && photometric == 2)
            {
                return new TiffRawFormat(TiffRawLayout.Float16Rgb, null);
            }

            if (samples == 1 && photometric == 1)
            {
                return new TiffRawFormat(TiffRawLayout.Float16Gray, null);
            }

            if (samples == 1 && photometric == 0)
            {
                return new TiffRawFormat(TiffRawLayout.Float16GrayInverted, null);
            }
        }

        if (bits == 8 && samples == 3 && photometric == 6 &&
            compression is 1 or 5 or 8 or 32946 or 32773 && !image.IsTiled())
        {
            var (subX, subY) = ReadSubsampling(image);
            if (subX > 1 || subY > 1)
            {
                return new TiffRawFormat(TiffRawLayout.YCbCrSub8, null, subX, subY);
            }
        }

        if (bits == 8 && samples == 3 && photometric == 6 && compression == 7)
        {
            EnableJpegRgbMode(image);
            return new TiffRawFormat(TiffRawLayout.Rgb, null);
        }

        if (bits == 8 && samples == 3 && photometric == 6 &&
            compression != 7 && HasUnitSubsampling(image))
        {
            return new TiffRawFormat(TiffRawLayout.YCbCr8, null);
        }

        if (bits == 8 && samples == 4 && photometric == 5)
        {
            return new TiffRawFormat(TiffRawLayout.Cmyk8, null);
        }

        if (bits == 8 && samples == 1 && photometric == 3 && compression != 7)
        {
            var palette = ReadColorMap(image);
            if (palette is { Length: >= 768 })
            {
                return new TiffRawFormat(TiffRawLayout.Palette8, palette);
            }

            return default;
        }

        if (bits == 16 && samples == 1 && photometric == 3 && compression != 7)
        {
            var palette = ReadColorMap(image);
            if (palette is { Length: >= 3 * 65536 })
            {
                return new TiffRawFormat(TiffRawLayout.Palette16, palette);
            }

            return default;
        }

        if (bits == 8 && samples == 4 && photometric == 2 && compression != 7 &&
            image.GetField(TiffTag.EXTRASAMPLES) is not null)
        {
            return new TiffRawFormat(TiffRawLayout.Rgba8, null);
        }

        if (bits == 16 && samples == 4 && photometric == 2 && compression != 7)
        {
            return new TiffRawFormat(TiffRawLayout.Rgba16, null);
        }

        if (samples == 3 && photometric == 2)
        {
            return new TiffRawFormat(bits == 16 ? TiffRawLayout.Rgb16 : TiffRawLayout.Rgb, null);
        }

        if (samples == 1 && photometric == 1)
        {
            return new TiffRawFormat(bits == 16 ? TiffRawLayout.Gray16 : TiffRawLayout.Gray, null);
        }

        if (samples == 1 && photometric == 0)
        {
            return new TiffRawFormat(bits == 16 ? TiffRawLayout.Gray16Inverted : TiffRawLayout.GrayInverted, null);
        }

        return default;
    }
}

internal enum TiffRawLayout
{
    None,
    Rgb,
    Rgb16,
    RgbPlanar8,
    RgbPlanar16,
    CmykPlanar8,
    CmykPlanar16,
    RgbaPlanar8,
    RgbaPlanar16,
    Cmyk8,
    Rgba8,
    Rgba16,
    Float32Gray,
    Float32GrayInverted,
    Float32Rgb,
    Float16Gray,
    Float16GrayInverted,
    Float16Rgb,
    Gray16Int,
    Rgb16Int,
    Palette8,
    Palette16,
    YCbCr8,
    YCbCrSub8,
    Gray,
    Gray16,
    GrayInverted,
    Gray16Inverted
}

internal readonly record struct TiffRawFormat(
    TiffRawLayout Layout,
    ushort[]? Palette,
    int SubX = 1,
    int SubY = 1);

