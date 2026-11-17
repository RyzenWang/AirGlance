using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task Keyboard_drives_navigation_edit_panels_and_fullscreen()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);

        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.CurrentIndex == 1);

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
        Assert.True(viewModel.InfoVisible);
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
        Assert.False(viewModel.FilmstripVisible);

        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.CurrentEdit.QuarterTurns == 1 && viewModel.StatusDimensions == "40 × 80");
        Assert.Equal("40 × 80", viewModel.StatusDimensions);

        window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.IsFullscreen);
        Assert.Equal(WindowState.FullScreen, window.WindowState);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !viewModel.IsFullscreen);
        Assert.NotEqual(WindowState.FullScreen, window.WindowState);
    }

    [AvaloniaFact]
    public async Task Language_shortcut_switches_without_reopening()
    {
        using var fixture = new WindowFixture();
        fixture.Window.Show();
        var viewModel = Assert.IsType<MainWindowViewModel>(fixture.Window.DataContext);
        await WaitUntilAsync(() => viewModel.HasImage);

        var before = fixture.Services.Localization.Language;
        fixture.Window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control | RawInputModifiers.Shift);
        await WaitUntilAsync(() => fixture.Services.Localization.Language != before);

        var after = fixture.Services.Localization.Language;
        Assert.NotEqual(before, after);
        Assert.True(fixture.Window.IsVisible);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for the window state.");
    }

    private sealed class WindowFixture : IDisposable
    {
        private readonly string _root;

        public WindowFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "airglance-window-" + Guid.NewGuid().ToString("N"));
            var images = Path.Combine(_root, "images");
            Directory.CreateDirectory(images);
            Directory.CreateDirectory(Path.Combine(_root, "settings"));
            Directory.CreateDirectory(Path.Combine(_root, "cache"));

            foreach (var name in new[] { "a.jpg", "b.jpg" })
            {
                File.Copy(
                    Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg"),
                    Path.Combine(images, name));
            }

            Services = new AppServices(Path.Combine(_root, "settings"), Path.Combine(_root, "cache"));
            Services.SetLanguage(LocalizationService.English);
            var options = CommandLineOptions.Parse([Path.Combine(images, "a.jpg")]);
            Window = new MainWindow(Services, options);
        }

        public AppServices Services { get; }

        public MainWindow Window { get; }

        public void Dispose()
        {
            Window.Close();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Window.IsVisible && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            Services.Dispose();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
