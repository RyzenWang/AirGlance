namespace AirGlance.Core.Storage;

/// <summary>
/// Everything AirGlance remembers between runs. The defaults are the values a first
/// launch should have; <see cref="Normalize"/> repairs a hand-edited or older file.
/// </summary>
public sealed class AppSettings
{
    public string Language { get; set; } = AppPaths.DefaultLanguage;

    /// <summary><c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = "System";

    /// <summary><c>name</c> or <c>name-descending</c>.</summary>
    public string Sort { get; set; } = "name";

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

    public bool SortDescending => string.Equals(Sort, "name-descending", StringComparison.Ordinal);

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

        Sort = SortDescending ? "name-descending" : "name";

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
