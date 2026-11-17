using AirGlance.App.Services;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AirGlance.App.Views;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow(LocalizationService localization)
    {
        InitializeComponent();
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F1)
        {
            Close();
            e.Handled = true;
        }
    }
}
