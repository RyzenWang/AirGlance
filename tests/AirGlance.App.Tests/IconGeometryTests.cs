using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class IconGeometryTests
{
    private static readonly string[] Keys =
    [
        "IconClose",
        "IconMinimize",
        "IconMaximize",
        "IconRestore",
        "IconInfo",
        "IconFilmstrip",
        "IconSettings",
        "IconFolder",
        "IconFolderPlus",
        "IconImage",
        "IconExternal",
        "IconChevronLeft",
        "IconChevronRight",
        "IconChevronDown",
        "IconWarning",
        "IconCheck",
        "IconMenu",
        "IconGithub",
        "IconMinus",
        "IconPlus",
        "IconFit",
        "IconRotate",
        "IconPlay",
        "IconFullscreen",
        "IconTheme",
        "IconMoon"
    ];

    [AvaloniaFact]
    public void Every_icon_stays_inside_the_24_unit_design_grid()
    {
        foreach (var key in Keys)
        {
            var geometry = Application.Current!.FindResource(key) as Geometry;
            Assert.True(geometry is not null, $"{key} is missing.");

            var bounds = geometry!.Bounds;
            Assert.True(bounds.Width > 1, $"{key} is empty.");
            Assert.True(
                bounds.X >= -0.5 && bounds.Y >= -0.5 &&
                bounds.Right <= 24.5 && bounds.Bottom <= 24.5,
                $"{key} leaves the 24 x 24 grid: {bounds}.");
        }
    }
}
