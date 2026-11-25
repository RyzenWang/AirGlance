namespace AirGlance.Core.Files;

public static class FolderScanner
{
    /// <summary>
    /// Lists displayable images directly inside one folder. Hidden and system files are
    /// skipped, only supported extensions are kept, and the result is naturally sorted.
    /// </summary>
    public static IReadOnlyList<string> EnumerateImages(
        string folder,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        var files = EnumerateMatching(folder, SupportedFormats.IsSupported, cancellationToken);
        files.Sort(NaturalPathComparer.Instance);

        if (descending)
        {
            files.Reverse();
        }

        return files;
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
