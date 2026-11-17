using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AirGlance.Core.Imaging;

public sealed class ImageHeader
{
    public static readonly ImageHeader Empty = new();

    public ExifDetails Exif { get; init; } = ExifDetails.Empty;

    public byte[]? IccProfile { get; init; }

    public int? BitDepth { get; init; }
}

/// <summary>
/// Reads the metadata blocks at the front of an image file without decoding pixels:
/// EXIF (JPEG APP1, PNG eXIf, WebP EXIF), the ICC profile (JPEG APP2, PNG iCCP,
/// WebP ICCP), and the source bit depth where it is meaningful (PNG).
/// </summary>
public static class ImageHeaderReader
{
    private const int MaxExifBytes = 1 * 1024 * 1024;
    private const int MaxIccBytes = 4 * 1024 * 1024;

    public static ImageHeader Read(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            Span<byte> signature = stackalloc byte[12];
            var read = stream.Read(signature);
            if (read < 2)
            {
                return ImageHeader.Empty;
            }

            stream.Position = 0;

            if (read >= 2 && signature[0] == 0xFF && signature[1] == 0xD8)
            {
                return ReadJpeg(stream);
            }

            if (read >= 8 && signature[..8].SequenceEqual(PngSignature))
            {
                return ReadPng(stream);
            }

            if (read >= 12 && signature[..4].SequenceEqual("RIFF"u8) && signature[8..12].SequenceEqual("WEBP"u8))
            {
                return ReadWebP(stream);
            }

            return ImageHeader.Empty;
        }
        catch
        {
            return ImageHeader.Empty;
        }
    }

    private static ImageHeader ReadJpeg(Stream stream)
    {
        byte[]? exif = null;
        var iccChunks = new Dictionary<int, (int Count, byte[] Data)>();
        stream.Position = 2;

        var header = new byte[4];
        while (true)
        {
            var prefix = stream.ReadByte();
            if (prefix < 0)
            {
                break;
            }

            if (prefix != 0xFF)
            {
                continue;
            }

            var marker = stream.ReadByte();
            while (marker == 0xFF)
            {
                marker = stream.ReadByte();
            }

            if (marker < 0 || marker == 0xD9 || marker == 0xDA)
            {
                break;
            }

            if (marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (stream.Read(header, 0, 2) != 2)
            {
                break;
            }

            var length = (header[0] << 8) | header[1];
            if (length < 2)
            {
                break;
            }

            var payload = length - 2;

            if (marker == 0xE1 && exif is null && payload is > 6 and <= MaxExifBytes)
            {
                var data = new byte[payload];
                if (stream.Read(data, 0, data.Length) != data.Length)
                {
                    break;
                }

                if (data.AsSpan(0, 6).SequenceEqual("Exif\0\0"u8))
                {
                    exif = data;
                }

                continue;
            }

            if (marker == 0xE2 && payload is > 16 and <= MaxIccBytes)
            {
                var data = new byte[payload];
                if (stream.Read(data, 0, data.Length) != data.Length)
                {
                    break;
                }

                if (data.AsSpan(0, 12).SequenceEqual("ICC_PROFILE\0"u8))
                {
                    int sequence = data[12];
                    int count = data[13];
                    if (sequence >= 1 && count >= 1 && iccChunks.Count < 32)
                    {
                        iccChunks[sequence] = (count, data);
                    }
                }

                continue;
            }

            if (stream.CanSeek)
            {
                stream.Seek(payload, SeekOrigin.Current);
            }
            else
            {
                var scratch = new byte[Math.Min(payload, 64 * 1024)];
                var remaining = payload;
                while (remaining > 0)
                {
                    var chunk = Math.Min(remaining, scratch.Length);
                    if (stream.Read(scratch, 0, chunk) != chunk)
                    {
                        remaining = 0;
                        break;
                    }

                    remaining -= chunk;
                }
            }
        }

        var icc = CombineIccChunks(iccChunks);
        return new ImageHeader
        {
            Exif = exif is null ? ExifDetails.Empty : ExifReader.Read(exif),
            IccProfile = icc,
            BitDepth = 8
        };
    }

    private static ImageHeader ReadPng(Stream stream)
    {
        stream.Position = 8;
        byte[]? icc = null;
        byte[]? exif = null;
        int? bitDepth = null;

        var chunkHeader = new byte[8];
        while (true)
        {
            if (stream.Read(chunkHeader, 0, 8) != 8)
            {
                break;
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(chunkHeader);
            var type = Encoding.ASCII.GetString(chunkHeader, 4, 4);
            if (length < 0 || length > 64 * 1024 * 1024)
            {
                break;
            }

            if (type == "IHDR" && length >= 13)
            {
                var data = new byte[length];
                if (stream.Read(data, 0, length) != length)
                {
                    break;
                }

                bitDepth = data[8];
                stream.Seek(4, SeekOrigin.Current);
                continue;
            }

            if (type == "iCCP" && icc is null && length is > 0 and <= MaxIccBytes)
            {
                var data = new byte[length];
                if (stream.Read(data, 0, length) != length)
                {
                    break;
                }

                icc = DecompressPngProfile(data);
                stream.Seek(4, SeekOrigin.Current);
                continue;
            }

            if (type == "eXIf" && exif is null && length is > 0 and <= MaxExifBytes)
            {
                var data = new byte[length];
                if (stream.Read(data, 0, length) != length)
                {
                    break;
                }

                exif = data;
                stream.Seek(4, SeekOrigin.Current);
                continue;
            }

            if (type == "IDAT" || type == "IEND")
            {
                break;
            }

            if (stream.CanSeek)
            {
                stream.Seek(length + 4L, SeekOrigin.Current);
            }
            else
            {
                break;
            }
        }

        return new ImageHeader
        {
            Exif = exif is null ? ExifDetails.Empty : ExifReader.Read(exif),
            IccProfile = icc,
            BitDepth = bitDepth
        };
    }

    private static ImageHeader ReadWebP(Stream stream)
    {
        stream.Position = 12;
        byte[]? icc = null;
        byte[]? exif = null;

        var chunkHeader = new byte[8];
        while (stream.Read(chunkHeader, 0, 8) == 8)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.AsSpan(4));
            var type = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            if (size < 0 || size > 64 * 1024 * 1024)
            {
                break;
            }

            if (type == "ICCP" && icc is null && size is > 0 and <= MaxIccBytes)
            {
                var data = new byte[size];
                if (stream.Read(data, 0, size) != size)
                {
                    break;
                }

                icc = data;
            }
            else if (type == "EXIF" && exif is null && size is > 0 and <= MaxExifBytes)
            {
                var data = new byte[size];
                if (stream.Read(data, 0, size) != size)
                {
                    break;
                }

                exif = data;
            }
            else if (!stream.CanSeek)
            {
                break;
            }
            else
            {
                stream.Seek(size, SeekOrigin.Current);
            }

            if ((size & 1) == 1 && stream.CanSeek)
            {
                stream.Seek(1, SeekOrigin.Current);
            }
        }

        return new ImageHeader
        {
            Exif = exif is null ? ExifDetails.Empty : ExifReader.Read(exif),
            IccProfile = icc,
            BitDepth = 8
        };
    }

    private static byte[]? DecompressPngProfile(byte[] data)
    {
        var nameEnd = Array.IndexOf(data, (byte)0);
        if (nameEnd < 0 || nameEnd + 2 > data.Length)
        {
            return null;
        }

        // The byte after the null-terminated name is the compression method; 0 means zlib.
        if (data[nameEnd + 1] != 0)
        {
            return null;
        }

        var start = nameEnd + 2;
        using var input = new MemoryStream(data, start, data.Length - start, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;
        int read;
        while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxIccBytes)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static byte[]? CombineIccChunks(Dictionary<int, (int Count, byte[] Data)> chunks)
    {
        if (chunks.Count == 0)
        {
            return null;
        }

        var expected = chunks.Values.Max(value => value.Count);
        if (expected != chunks.Count || chunks.Keys.Min() != 1 || chunks.Keys.Max() != expected)
        {
            // A partial profile cannot be interpreted; use the decoder's own default instead.
            return null;
        }

        using var output = new MemoryStream();
        for (var index = 1; index <= expected; index++)
        {
            if (!chunks.TryGetValue(index, out var chunk) || chunk.Data.Length <= 14)
            {
                return null;
            }

            output.Write(chunk.Data, 14, chunk.Data.Length - 14);
        }

        return output.ToArray();
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}
