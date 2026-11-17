using AirGlance.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AirGlance.App.Views;

public partial class SaveCopyDialog : Window
{
    private readonly SaveCopyViewModel _viewModel;

    public SaveCopyDialog(SaveCopyViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        DialogChrome.Apply(this, HeaderGrid, HeaderCloseButton);
        _viewModel.Saved += OnSaved;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch
        {
            // The dialog still lets the user cancel.
        }
    }

    private void OnSaved(object? sender, string path) => Close(path);

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Saved -= OnSaved;
        _viewModel.Dispose();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = _viewModel.BrowseTitle,
                AllowMultiple = false
            });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.SetFolder(path);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
