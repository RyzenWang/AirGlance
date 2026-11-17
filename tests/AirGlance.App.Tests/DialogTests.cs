using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.App.Views;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class DialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airglance-dialog-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _services;

    public DialogTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "settings"));
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
        _services = new AppServices(Path.Combine(_root, "settings"), Path.Combine(_root, "cache"));
        _services.SetLanguage(LocalizationService.English);
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [AvaloniaFact]
    public void Settings_window_loads_and_applies_changes()
    {
        var window = new SettingsWindow(_services);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
        viewModel.ThemeIndex = 2;
        viewModel.Checkerboard = false;

        Assert.Equal("Dark", _services.Settings.Theme);
        Assert.False(_services.Settings.Checkerboard);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Save_copy_dialog_estimates_and_saves()
    {
        var source = Path.Combine(_root, "photo.jpg");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg"), source);
        var encoded = new byte[] { 1, 2, 3, 4, 5 };

        var viewModel = new SaveCopyViewModel(
            source,
            _services.Localization,
            (_, _) => Task.FromResult(encoded));

        var window = new SaveCopyDialog(viewModel);
        window.Show();
        await viewModel.InitializeAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("about", viewModel.EstimateText);
        Assert.Equal(Path.Combine(_root, "photo-copy.jpg"), viewModel.FilePath);

        window.Close();
    }

    [AvaloniaFact]
    public void Shortcuts_and_message_windows_load()
    {
        var shortcuts = new ShortcutsWindow(_services.Localization);
        shortcuts.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shortcuts.IsVisible);
        shortcuts.Close();

        var message = new MessageWindow("AirGlance 0.1.0", "Command line help");
        message.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(message.IsVisible);
        message.Close();
    }
}
