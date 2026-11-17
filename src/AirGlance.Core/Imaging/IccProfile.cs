using System.Text;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Reads the human-readable description out of an ICC profile, which is what lets the
/// information panel say "Display P3 → sRGB" instead of just "converted".
/// </summary>
public static class IccProfile
{
    public static string? ReadDescription(byte[]? profile)
    {
        if (profile is null || profile.Length < 132)
        {
            return null;
        }

        try
        {
            var tagCount = ReadUInt32(profile, 128);
            if (tagCount is 0 or > 512)
            {
                return null;
            }

            var tags = new Dictionary<string, (int Offset, int Size)>(StringComparer.Ordinal);
            for (var index = 0; index < tagCount; index++)
            {
                var entry = 132 + index * 12;
                if (entry + 12 > profile.Length)
                {
                    break;
                }

                var signature = Encoding.ASCII.GetString(profile, entry, 4);
                var offset = ReadUInt32(profile, entry + 4);
                var size = ReadUInt32(profile, entry + 8);
                if (offset <= int.MaxValue && size <= int.MaxValue && offset + (long)size <= profile.Length)
                {
                    tags[signature] = ((int)offset, (int)size);
                }
            }

            foreach (var signature in new[] { "desc", "cprt" })
            {
                if (tags.TryGetValue(signature, out var tag))
                {
                    var description = ReadTagText(profile, tag.Offset, tag.Size);
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        return FriendlyName(description.Trim());
                    }
                }
            }
        }
        catch
        {
            // An unreadable profile is a missing label, not an image failure.
        }

        return null;
    }

    /// <summary>
    /// Some compact profiles carry a four-character code rather than a name. Translate the
    /// few codes a photographer would otherwise have to look up.
    /// </summary>
    private static string FriendlyName(string description) => description switch
    {
        "A98C" => "Adobe RGB (1998)",
        "A98" => "Adobe RGB (1998)",
        "sP3" or "P3" => "Display P3",
        _ => description
    };

    private static string? ReadTagText(byte[] profile, int offset, int size)
    {
        if (offset + 8 > profile.Length || size < 12)
        {
            return null;
        }

        var type = Encoding.ASCII.GetString(profile, offset, 4);
        switch (type)
        {
            case "desc":
            {
                var count = ReadUInt32(profile, offset + 8);
                var start = offset + 12;
                if (count == 0 || start + count > profile.Length)
                {
                    return null;
                }

                return DecodeAscii(profile.AsSpan(start, (int)Math.Min(count, (uint)(profile.Length - start))));
            }

            case "text":
                return DecodeAscii(profile.AsSpan(offset + 8, size - 8));

            case "mluc":
            {
                var count = ReadUInt32(profile, offset + 8);
                if (count is 0 or > 64)
                {
                    return null;
                }

                var recordSize = ReadUInt32(profile, offset + 12);
                var record = offset + 16;
                if (recordSize < 12)
                {
                    return null;
                }

                for (var index = 0; index < count; index++)
                {
                    var entry = record + (int)(index * recordSize);
                    if (entry + 12 > profile.Length)
                    {
                        break;
                    }

                    var length = ReadUInt32(profile, entry + 4);
                    var textOffset = ReadUInt32(profile, entry + 8);
                    var start = offset + (int)textOffset;
                    if (start + (long)length > profile.Length || length == 0 || length % 2 != 0)
                    {
                        continue;
                    }

                    var text = Encoding.BigEndianUnicode.GetString(profile, start, (int)length);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }

                return null;
            }

            default:
                return null;
        }
    }

    private static string? DecodeAscii(ReadOnlySpan<byte> bytes)
    {
        var zero = bytes.IndexOf((byte)0);
        if (zero >= 0)
        {
            bytes = bytes[..zero];
        }

        var text = Encoding.ASCII.GetString(bytes).Trim();
        return text.Length == 0 ? null : text;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
}
