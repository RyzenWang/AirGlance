using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AirGlance.App.Styles;

/// <summary>
/// The high-contrast palette as a compiled resource dictionary. Compiled rather than
/// loaded by URI so trimming and NativeAOT cannot lose the XAML lookup.
/// </summary>
public partial class HighContrastPalette : ResourceDictionary
{
    public HighContrastPalette() => AvaloniaXamlLoader.Load(this);
}
