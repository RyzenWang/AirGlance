using AirGlance.App.Controls;
using AirGlance.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AirGlance.App.Views;

public partial class AnalysisPanelView : UserControl
{
    public AnalysisPanelView()
    {
        InitializeComponent();
    }

    public AnalysisPanelView(AnalysisViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private AnalysisViewModel? ViewModel => DataContext as AnalysisViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => ViewModel?.RequestClose();

    private void OnHistogramTab(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectTab(AnalysisTab.Histogram);

    private void OnColorTab(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectTab(AnalysisTab.Color);

    private void OnQualityTab(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectTab(AnalysisTab.Quality);

    private void OnDataTab(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectTab(AnalysisTab.Data);

    private void OnLumaMode(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectMode(HistogramChartMode.Luma);

    private void OnRgbMode(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectMode(HistogramChartMode.Rgb);

    private void OnHueMode(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectMode(HistogramChartMode.Hue);

    private void OnSaturationMode(object? sender, RoutedEventArgs e) =>
        ViewModel?.SelectMode(HistogramChartMode.Saturation);
}
