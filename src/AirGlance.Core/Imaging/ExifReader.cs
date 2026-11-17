using System.Globalization;
using System.Text;

namespace AirGlance.Core.Imaging;

/// <summary>
/// A small EXIF (TIFF) reader. It reads the handful of tags the information panel shows
/// and ignores everything else; malformed files return empty details rather than throwing.
/// </summary>
public static class ExifReader
{
    private static readonly byte[] ExifHeader = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];

    public static ExifDetails Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return ExifDetails.Empty;
        }

        var data = payload.ToArray();
        var start = 0;

        if (data.Length >= ExifHeader.Length && data.AsSpan(0, ExifHeader.Length).SequenceEqual(ExifHeader))
        {
            start = ExifHeader.Length;
        }

        try
        {
            return ReadTiff(data, start);
        }
        catch
        {
            // A broken metadata block must never stop the image from opening.
            return ExifDetails.Empty;
        }
    }

    private static ExifDetails ReadTiff(byte[] data, int start)
    {
        if (data.Length - start < 8)
        {
            return ExifDetails.Empty;
        }

        var little = data[start] switch
        {
            0x49 when data[start + 1] == 0x49 => true,
            0x4D when data[start + 1] == 0x4D => false,
            _ => throw new InvalidDataException("Not a TIFF header.")
        };

        var reader = new TiffReader(data, start, little);
        if (reader.ReadUInt16(2) != 42)
        {
            return ExifDetails.Empty;
        }

        var ifd0 = reader.ReadUInt32(4);
        if (ifd0 == 0)
        {
            return ExifDetails.Empty;
        }

        var tags = reader.ReadIfd((int)ifd0);
        var exifIfd = reader.ReadUInt32Value(tags, 0x8769);
        var exifTags = exifIfd is > 0 and <= int.MaxValue
            ? reader.ReadIfd((int)exifIfd.Value)
            : new Dictionary<ushort, TiffEntry>();

        string? make = reader.ReadAscii(tags, 0x010F);
        string? model = reader.ReadAscii(tags, 0x0110);
        string? dateTime = reader.ReadAscii(tags, 0x0132);
        var orientation = reader.ReadNumber(tags, 0x0112);

        // Cameras normally put exposure data in the Exif sub-IFD, but some writers
        // (including Pillow) leave it in IFD0. Look in both, preferring the sub-IFD.
        string? lens = reader.ReadAscii(exifTags, 0xA434) ?? reader.ReadAscii(tags, 0xA434);
        var exposure = reader.ReadRational(exifTags, 0x829A) ?? reader.ReadRational(tags, 0x829A);
        var fNumber = reader.ReadRational(exifTags, 0x829D) ?? reader.ReadRational(tags, 0x829D);
        var iso = reader.ReadNumber(exifTags, 0x8827) ?? reader.ReadNumber(tags, 0x8827);
        var focal = reader.ReadRational(exifTags, 0x920A) ?? reader.ReadRational(tags, 0x920A);
        var bias = reader.ReadSignedRational(exifTags, 0x9204) ?? reader.ReadSignedRational(tags, 0x9204);
        var taken = reader.ReadAscii(exifTags, 0x9003)
            ?? reader.ReadAscii(tags, 0x9003)
            ?? dateTime;

        DateTime? takenAt = null;
        if (!string.IsNullOrWhiteSpace(taken) &&
            DateTime.TryParseExact(
                taken.Trim(),
                "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            takenAt = parsed;
        }

        return new ExifDetails
        {
            CameraMake = Clean(make),
            CameraModel = Clean(model),
            LensModel = Clean(lens),
            ExposureSeconds = exposure is > 0 ? exposure : null,
            FNumber = fNumber is > 0 ? fNumber : null,
            Iso = iso is > 0 and <= int.MaxValue ? (int)iso.Value : null,
            FocalLengthMillimeters = focal is > 0 ? focal : null,
            ExposureBias = bias,
            TakenAt = takenAt,
            TakenAtRaw = Clean(taken),
            Orientation = orientation is > 0 and <= 8 ? (int)orientation.Value : null
        };
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('\0').Trim();
    }

    private sealed class TiffReader
    {
        private readonly byte[] _data;
        private readonly int _start;
        private readonly bool _little;

        public TiffReader(byte[] data, int start, bool little)
        {
            _data = data;
            _start = start;
            _little = little;
        }

        public ushort ReadUInt16(int offset)
        {
            var index = _start + offset;
            return _little
                ? (ushort)(_data[index] | (_data[index + 1] << 8))
                : (ushort)((_data[index] << 8) | _data[index + 1]);
        }

        public uint ReadUInt32(int offset)
        {
            var index = _start + offset;
            return _little
                ? (uint)(_data[index] | (_data[index + 1] << 8) | (_data[index + 2] << 16) | (_data[index + 3] << 24))
                : (uint)((_data[index] << 24) | (_data[index + 1] << 16) | (_data[index + 2] << 8) | _data[index + 3]);
        }

        public Dictionary<ushort, TiffEntry> ReadIfd(int offset)
        {
            var entries = new Dictionary<ushort, TiffEntry>();
            if (offset < 0 || _start + offset + 2 > _data.Length)
            {
                return entries;
            }

            var count = ReadUInt16(offset);
            if (count > 512)
            {
                return entries;
            }

            for (var index = 0; index < count; index++)
            {
                var entryOffset = offset + 2 + index * 12;
                if (_start + entryOffset + 12 > _data.Length)
                {
                    break;
                }

                var tag = ReadUInt16(entryOffset);
                var type = ReadUInt16(entryOffset + 2);
                var valueCount = ReadUInt32(entryOffset + 4);
                entries[tag] = new TiffEntry(type, valueCount, entryOffset + 8);
            }

            return entries;
        }

        public string? ReadAscii(Dictionary<ushort, TiffEntry> tags, ushort tag)
        {
            if (!tags.TryGetValue(tag, out var entry) || entry.Type is not (2 or 7))
            {
                return null;
            }

            var offset = ResolveValueOffset(entry);
            if (offset < 0)
            {
                return null;
            }

            var available = _data.Length - _start - offset;
            var length = (int)Math.Min(entry.Count, (uint)Math.Max(0, available));
            if (length <= 0)
            {
                return null;
            }

            var bytes = _data.AsSpan(_start + offset, length);
            var zero = bytes.IndexOf((byte)0);
            if (zero >= 0)
            {
                bytes = bytes[..zero];
            }

            return Encoding.UTF8.GetString(bytes).Trim();
        }

        public uint? ReadUInt32Value(Dictionary<ushort, TiffEntry> tags, ushort tag)
        {
            if (!tags.TryGetValue(tag, out var entry))
            {
                return null;
            }

            var offset = ResolveValueOffset(entry);
            return offset < 0 ? null : ReadUInt32(offset);
        }

        public double? ReadNumber(Dictionary<ushort, TiffEntry> tags, ushort tag)
        {
            if (!tags.TryGetValue(tag, out var entry) || entry.Count == 0)
            {
                return null;
            }

            var offset = ResolveValueOffset(entry);
            if (offset < 0)
            {
                return null;
            }

            return entry.Type switch
            {
                3 => ReadUInt16(offset),
                4 => ReadUInt32(offset),
                _ => null
            };
        }

        public double? ReadRational(Dictionary<ushort, TiffEntry> tags, ushort tag) =>
            ReadRationalCore(tags, tag);

        public double? ReadSignedRational(Dictionary<ushort, TiffEntry> tags, ushort tag) =>
            ReadRationalCore(tags, tag);

        private double? ReadRationalCore(Dictionary<ushort, TiffEntry> tags, ushort tag)
        {
            if (!tags.TryGetValue(tag, out var entry) || entry.Count == 0)
            {
                return null;
            }

            var offset = ResolveValueOffset(entry);
            if (offset < 0)
            {
                return null;
            }

            double numerator;
            double denominator;

            switch (entry.Type)
            {
                case 5:
                    numerator = ReadUInt32(offset);
                    denominator = ReadUInt32(offset + 4);
                    break;
                case 10:
                    numerator = (int)ReadUInt32(offset);
                    denominator = (int)ReadUInt32(offset + 4);
                    break;
                // Pillow writes "1/250" as two SHORTs and exposure bias as two SSHORTs.
                case 3 when entry.Count >= 2:
                    numerator = ReadUInt16(offset);
                    denominator = ReadUInt16(offset + 2);
                    break;
                case 8 when entry.Count >= 2:
                    numerator = (short)ReadUInt16(offset);
                    denominator = (short)ReadUInt16(offset + 2);
                    break;
                default:
                    return null;
            }

            return denominator == 0 ? null : numerator / denominator;
        }

        /// <summary>Returns the value's position relative to the TIFF header.</summary>
        private int ResolveValueOffset(TiffEntry entry)
        {
            var size = TypeSize(entry.Type) * (long)entry.Count;
            if (size <= 0)
            {
                return -1;
            }

            var offset = size <= 4 ? entry.FieldOffset : (int)ReadUInt32(entry.FieldOffset);
            if (offset < 0 || _start + offset + size > _data.Length)
            {
                return -1;
            }

            return offset;
        }

        private static int TypeSize(ushort type) => type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 11 => 4,
            5 or 10 or 12 => 8,
            _ => 0
        };
    }

    private readonly record struct TiffEntry(ushort Type, uint Count, int FieldOffset);
}

