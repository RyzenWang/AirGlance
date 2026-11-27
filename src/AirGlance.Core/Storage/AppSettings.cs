using AirGlance.Core.Files;

namespace AirGlance.Core.Storage;

/// <summary>
/// Everything AirGlance remembers between runs. The defaults are the values a first
/// launch should have; <see cref="Normalize"/> repairs a hand-edited or older file.
/// </summary>
public sealed class AppSettings
{
    /// <summary>How many folders the recent list keeps.</summary>
    public const int RecentFolderLimit = 10;

    public string Language { get; set; } = AppPaths.DefaultLanguage;

    /// <summary><c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// <c>name</c>, <c>date</c>, <c>size</c> or <c>type</c>, each optionally suffixed with
    /// <c>-descending</c>.
    /// </summary>
    public string Sort { get; set; } = "name";

    /// <summary>Folders opened recently, newest first. Not a library: paths only.</summary>
    public List<string> RecentFolders { get; set; } = [];

    /// <summary>Slideshow interval in seconds: 3, 5 or 10.</summary>
    public int SlideshowSeconds { get; set; } = 5;

    public bool RememberZoom { get; set; } = true;

    /// <summary><c>fit</c>, <c>fit-width</c>, <c>actual</c> or <c>manual</c>.</summary>
    public string ZoomMode { get; set; } = "fit";

    public double ZoomFactor { get; set; } = 1.0;

    public bool Checkerboard { get; set; } = true;

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 860;

    public bool WindowMaximized { get; set; }

    public bool SortDescending => FolderSortOrder.Parse(Sort).Descending;

    public FolderSortOrder SortOrder => FolderSortOrder.Parse(Sort);

    /// <summary>
    /// Records a folder at the top of the recent list, keeping one entry per path and the
    /// list bounded. Returns true when the list changed, so callers can skip a save.
    /// </summary>
    public bool RememberFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var path = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Length == 0)
        {
            path = folder;
        }

        if (RecentFolders.Count > 0 &&
            string.Equals(RecentFolders[0], path, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        RecentFolders.RemoveAll(
            entry => string.Equals(entry, path, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, path);
        if (RecentFolders.Count > RecentFolderLimit)
        {
            RecentFolders.RemoveRange(
                RecentFolderLimit,
                RecentFolders.Count - RecentFolderLimit);
        }

        return true;
    }

    public AppSettings Normalize()
    {
        Language = string.Equals(Language, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
                   (Language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ?? false)
            ? "zh-CN"
            : "en";

        Theme = Theme switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "System"
        };

        Sort = FolderSortOrder.Parse(Sort).ToSettingValue();

        var recent = new List<string>(RecentFolderLimit);
        foreach (var folder in RecentFolders ?? [])
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            if (recent.Any(entry => string.Equals(entry, folder, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            recent.Add(folder);
            if (recent.Count == RecentFolderLimit)
            {
                break;
            }
        }

        RecentFolders = recent;

        if (SlideshowSeconds is not (3 or 5 or 10))
        {
            SlideshowSeconds = 5;
        }

        ZoomMode = ZoomMode switch
        {
            "fit-width" => "fit-width",
            "actual" => "actual",
            "manual" => "manual",
            _ => "fit"
        };

        if (double.IsNaN(ZoomFactor) || double.IsInfinity(ZoomFactor))
        {
            ZoomFactor = 1.0;
        }

        ZoomFactor = Math.Clamp(ZoomFactor, 0.01, 32.0);

        if (double.IsNaN(WindowWidth) || WindowWidth < 480)
        {
            WindowWidth = 1240;
        }

        if (double.IsNaN(WindowHeight) || WindowHeight < 360)
        {
            WindowHeight = 860;
        }

        WindowWidth = Math.Clamp(WindowWidth, 480, 10000);
        WindowHeight = Math.Clamp(WindowHeight, 360, 10000);

        return this;
    }
}
