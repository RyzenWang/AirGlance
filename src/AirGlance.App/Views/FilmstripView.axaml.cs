using AirGlance.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AirGlance.App.Views;

public partial class FilmstripView : UserControl
{
    public FilmstripView()
    {
        InitializeComponent();
        FilmstripList.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    public void ScrollTo(int index)
    {
        if (index >= 0)
        {
            FilmstripList.ScrollIntoView(index);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            FilmstripList.SelectedIndex >= 0 &&
            FilmstripList.SelectedIndex != viewModel.CurrentIndex)
        {
            _ = viewModel.GoToAsync(FilmstripList.SelectedIndex);
        }
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var scrollViewer = FilmstripList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        var target = scrollViewer.Offset.X - e.Delta.Y * 80;
        var maximum = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        scrollViewer.Offset = new Vector(Math.Clamp(target, 0, maximum), 0);
        e.Handled = true;
    }
}
