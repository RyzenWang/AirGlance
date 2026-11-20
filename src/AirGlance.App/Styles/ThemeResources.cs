using Avalonia;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace AirGlance.App.Styles;

/// <summary>
/// Resolves a themed brush from the application resources at the current theme
/// variant. This is for controls built in code, where a local DynamicResource
/// binding is not available.
/// </summary>
internal static class ThemeResources
{
    public static void SetDynamic(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, new DynamicResourceExtension(key));

    public static IBrush Brush(string key, Color fallback)
    {
        if (Application.Current is { } app &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
