namespace AirGlance.Core.Files;

public sealed record ImageSource(string FolderPath, IReadOnlyList<string> Files, int Index)
{
    public string? CurrentPath => Index >= 0 && Index < Files.Count ? Files[Index] : null;
}

/// <summary>
/// Turns a command-line, dropped, or picked path into "this folder, these files,
/// starting here". A folder opens its first supported image; a file opens its folder
/// and selects itself, so left/right work immediately.
/// </summary>
public static class ImageSourceResolver
{
    /// <summary>Name-ordered overload kept for callers that only know about direction.</summary>
    public static ImageSource Resolve(
        string path,
        bool descending = false,
        CancellationToken cancellationToken = default) =>
        Resolve(path, new FolderSortOrder(FolderSortField.Name, descending), cancellationToken);

    public static ImageSource Resolve(
        string path,
        FolderSortOrder order,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);

        if (Directory.Exists(fullPath))
        {
            var files = FolderScanner.EnumerateImages(fullPath, order, cancellationToken);
            return new ImageSource(fullPath, files, 0);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The file does not exist.", fullPath);
        }

        var folder = Path.GetDirectoryName(fullPath) ?? fullPath;
        var index = 0;
        IReadOnlyList<string> siblings;

        try
        {
            siblings = FolderScanner.EnumerateImages(folder, order, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            siblings = [fullPath];
        }

        var match = -1;
        for (var candidate = 0; candidate < siblings.Count; candidate++)
        {
            if (string.Equals(siblings[candidate], fullPath, StringComparison.OrdinalIgnoreCase))
            {
                match = candidate;
                break;
            }
        }

        // A directly opened file that the folder scan skipped (hidden, for example)
        // still opens, on its own, instead of failing.
        if (match >= 0)
        {
            index = match;
        }
        else
        {
            siblings = [fullPath];
        }

        return new ImageSource(folder, siblings, index);
    }
}
