namespace AirGlance.Core.Files;

/// <summary>
/// Orders file names the way people read them: <c>img2.jpg</c> before <c>img10.jpg</c>,
/// and <c>img02.jpg</c> next to <c>img2.jpg</c> rather than before every other name.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var result = CompareNatural(x, y);
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }

    private static int CompareNatural(string left, string right)
    {
        var i = 0;
        var j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                var leftStart = i;
                var rightStart = j;

                while (i < left.Length && char.IsAsciiDigit(left[i]))
                {
                    i++;
                }

                while (j < right.Length && char.IsAsciiDigit(right[j]))
                {
                    j++;
                }

                var leftSignificant = SkipZeros(left, leftStart, i);
                var rightSignificant = SkipZeros(right, rightStart, j);
                var leftLength = i - leftSignificant;
                var rightLength = j - rightSignificant;

                if (leftLength != rightLength)
                {
                    return leftLength.CompareTo(rightLength);
                }

                for (var k = 0; k < leftLength; k++)
                {
                    var digit = left[leftSignificant + k].CompareTo(right[rightSignificant + k]);
                    if (digit != 0)
                    {
                        return digit;
                    }
                }

                // Equal numeric value: the shorter run (fewer leading zeros) comes first.
                var leftRun = i - leftStart;
                var rightRun = j - rightStart;
                if (leftRun != rightRun)
                {
                    return leftRun.CompareTo(rightRun);
                }

                continue;
            }

            var leftChar = char.ToUpperInvariant(left[i]);
            var rightChar = char.ToUpperInvariant(right[j]);
            if (leftChar != rightChar)
            {
                return leftChar.CompareTo(rightChar);
            }

            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }

    private static int SkipZeros(string value, int start, int end)
    {
        while (start < end && value[start] == '0')
        {
            start++;
        }

        return start;
    }
}

/// <summary>Compares full paths by file name, so a folder listing follows display order.</summary>
public sealed class NaturalPathComparer : IComparer<string>
{
    public static readonly NaturalPathComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var nameX = x is null ? null : Path.GetFileName(x);
        var nameY = y is null ? null : Path.GetFileName(y);
        var result = NaturalStringComparer.Instance.Compare(nameX, nameY);
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }
}
