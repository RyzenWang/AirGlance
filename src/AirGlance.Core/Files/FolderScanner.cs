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

            if (SupportedFormats.IsSupported(file))
            {
                files.Add(file);
            }
        }

        files.Sort(NaturalPathComparer.Instance);

        if (descending)
        {
            files.Reverse();
        }

        return files;
    }
}
