namespace AirGlance.Core.Files;

public static class FolderScanner
{
    /// <summary>
    /// Lists displayable images directly inside one folder. Hidden and system files are
    /// skipped, only supported extensions are kept, and the result is ordered by
    /// <paramref name="order"/> — the natural name order unless the caller asked for
    /// modification time, size or type.
    /// </summary>
    public static IReadOnlyList<string> EnumerateImages(
        string folder,
        FolderSortOrder order,
        CancellationToken cancellationToken = default)
    {
        var files = EnumerateMatching(folder, SupportedFormats.IsSupported, cancellationToken);
        SortFiles(files, order);
        return files;
    }

    /// <summary>Name-ordered overload kept for callers that only know about direction.</summary>
    public static IReadOnlyList<string> EnumerateImages(
        string folder,
        bool descending = false,
        CancellationToken cancellationToken = default) =>
        EnumerateImages(folder, new FolderSortOrder(FolderSortField.Name, descending), cancellationToken);

    private static void SortFiles(List<string> files, FolderSortOrder order)
    {
        switch (order.Field)
        {
            case FolderSortField.DateModified:
                files.Sort((left, right) => CompareBy(
                    File.GetLastWriteTimeUtc(left).Ticks,
                    File.GetLastWriteTimeUtc(right).Ticks,
                    left,
                    right));
                break;

            case FolderSortField.Size:
                files.Sort((left, right) => CompareBy(
                    new FileInfo(left).Length,
                    new FileInfo(right).Length,
                    left,
                    right));
                break;

            case FolderSortField.Type:
                files.Sort((left, right) =>
                {
                    var byExtension = string.Compare(
                        Path.GetExtension(left),
                        Path.GetExtension(right),
                        StringComparison.OrdinalIgnoreCase);
                    return byExtension != 0
                        ? byExtension
                        : NaturalStringComparer.Instance.Compare(
                            Path.GetFileName(left),
                            Path.GetFileName(right));
                });
                break;

            default:
                files.Sort(NaturalStringComparer.Instance);
                break;
        }

        if (order.Descending)
        {
            files.Reverse();
        }
    }

    /// <summary>
    /// Orders by one numeric key and falls back to the natural file name so that two files
    /// with the same timestamp or size always appear in the same, predictable order.
    /// </summary>
    private static int CompareBy(long leftKey, long rightKey, string left, string right)
    {
        var byKey = leftKey.CompareTo(rightKey);
        return byKey != 0
            ? byKey
            : NaturalStringComparer.Instance.Compare(Path.GetFileName(left), Path.GetFileName(right));
    }

    /// <summary>
    /// Counts the files in the folder that AirGlance recognises but cannot decode, so the
    /// viewer can say why a folder of HEIC or RAW files appears empty instead of leaving
    /// the user to guess.
    /// </summary>
    public static int CountUnsupported(string folder, CancellationToken cancellationToken = default) =>
        EnumerateMatching(folder, SupportedFormats.IsKnownUnsupported, cancellationToken).Count;

    private static List<string> EnumerateMatching(
        string folder,
        Func<string, bool> predicate,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(folder);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false,
            MatchCasing = MatchCasing.PlatformDefault
        };

        var files = new List<string>();
        var seen = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            if ((seen++ & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (predicate(file))
            {
                // On Unix a leading dot marks a hidden file. Windows reports the hidden
                // attribute instead, and AttributesToSkip already removes it.
                if (!OperatingSystem.IsWindows() &&
                    Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        return files;
    }
}
