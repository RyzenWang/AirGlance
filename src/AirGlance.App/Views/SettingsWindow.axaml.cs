using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AirGlance.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(services);
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();

    private void OnGitHubClick(object? sender, RoutedEventArgs e) =>
        AppServices.OpenExternalUrl("https://github.com/RyzenWang/AirGlance");

    private void OnThirdPartyClick(object? sender, RoutedEventArgs e) =>
        AppServices.OpenExternalUrl("https://github.com/RyzenWang/AirGlance/blob/main/THIRD-PARTY-NOTICES.md");
}
