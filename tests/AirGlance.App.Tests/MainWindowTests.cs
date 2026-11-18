using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        Assert.True(viewModel.FilmstripVisible);
        Assert.False(viewModel.InfoVisible);

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

    [AvaloniaFact]
    public async Task Canvas_receives_pointer_hits_through_the_overlay_layers()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        window.Show();
        await WaitUntilAsync(() => window.IsVisible);
        Dispatcher.UIThread.RunJobs();

        var visuals = window.GetVisualsAt(new Point(300, 260)).ToList();
        Assert.Contains(visuals, visual => visual is Border border && border.Classes.Contains("canvas"));
    }

    [AvaloniaFact]
    public async Task Wheel_zooms_at_the_pointer_and_does_not_page()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);
        Dispatcher.UIThread.RunJobs();

        var viewer = window.GetVisualDescendants().OfType<ImageViewer>().First();
        var index = viewModel.CurrentIndex;
        Assert.Equal(ViewerZoomMode.FitWindow, viewer.ZoomMode);

        var wheel = new PointerWheelEventArgs(
            viewer,
            new Pointer(1, PointerType.Mouse, true),
            viewer,
            new Point(620, 430),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(0, -1));
        viewer.RaiseEvent(wheel);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(index, viewModel.CurrentIndex);
        Assert.Equal(ViewerZoomMode.Manual, viewer.ZoomMode);
    }

    [AvaloniaFact]
    public async Task Theme_button_swaps_between_sun_and_moon()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        window.Show();
        await WaitUntilAsync(() => window.IsVisible);
        Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants()
            .OfType<Button>()
            .First(candidate => candidate.Classes.Contains("window-tool"));
        var icon = Assert.IsType<IconPath>(button.Content);
        Assert.Same(window.FindResource("IconTheme"), icon.Data);

        fixture.Services.SetTheme(ThemeService.DarkMode);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(window.FindResource("IconMoon"), icon.Data);
        Assert.Equal("Switch to light", ToolTip.GetTip(button));

        fixture.Services.SetTheme(ThemeService.LightMode);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(window.FindResource("IconTheme"), icon.Data);
        Assert.Equal("Switch to dark", ToolTip.GetTip(button));
    }

    [AvaloniaFact]
    public async Task Animated_gif_is_presented_as_an_animation()
    {
        using var fixture = new WindowFixture("animated.gif");
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        var viewer = window.GetVisualDescendants().OfType<ImageViewer>().First();
        Assert.NotNull(viewer.Source);
        var first = viewer.Source;
        await WaitUntilAsync(() => !ReferenceEquals(first, viewer.Source), 3_000);
    }

    [AvaloniaFact]
    public async Task Svg_is_presented_as_an_image()
    {
        using var fixture = new WindowFixture("vector.svg");
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        var viewer = window.GetVisualDescendants().OfType<ImageViewer>().First();
        Assert.NotNull(viewer.Source);
        Assert.Equal(64, viewer.SourcePixelWidth);
        Assert.Equal(48, viewer.SourcePixelHeight);
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

        public WindowFixture(string? initialAsset = null)
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

            if (initialAsset is not null)
            {
                File.Copy(
                    Path.Combine(AppContext.BaseDirectory, "Assets", initialAsset),
                    Path.Combine(images, initialAsset));
            }

            Services = new AppServices(Path.Combine(_root, "settings"), Path.Combine(_root, "cache"));
            Services.SetLanguage(LocalizationService.English);
            var options = CommandLineOptions.Parse([Path.Combine(images, initialAsset ?? "a.jpg")]);
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
