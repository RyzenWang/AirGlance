using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AirGlance.App.Views;

/// <summary>
/// Gives a dialog the same chrome as the shell: the native title bar is removed,
/// the custom header drags the window, and the header close button replaces the
/// system one. macOS and Linux keep their native title bar.
/// </summary>
internal static class DialogChrome
{
    public static void Apply(Window window, Control header, Control closeButton)
    {
        if (!OperatingSystem.IsWindows())
        {
            closeButton.IsVisible = false;
            return;
        }

        window.SystemDecorations = SystemDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = 58;

        header.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (e.Source is Visual source &&
                (source is Button || source.GetVisualAncestors().OfType<Button>().Any()))
            {
                return;
            }

            if (e.ClickCount == 1)
            {
                window.BeginMoveDrag(e);
            }
        };
    }
}
