using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    public async Task Analysis_drawer_loads_and_escape_closes_it()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Histogram);
        Assert.True(viewModel.AnalysisVisible);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);
        Assert.NotEmpty(viewModel.Analysis.Display.Luma);
        Assert.True(viewModel.Analysis.Display.Luma.Sum() > 0);
        Assert.Equal("Analysis", viewModel.Analysis.Labels.Title);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !viewModel.AnalysisVisible);
        Assert.False(viewModel.Analysis.IsEyedropperActive);
    }

    [AvaloniaFact]
    public async Task Dual_view_closes_the_analysis_drawer()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Quality);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);
        Assert.True(viewModel.AnalysisVisible);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() => !viewModel.AnalysisVisible);
        Assert.False(viewModel.AnalysisVisible);
    }

    [AvaloniaFact]
    public async Task Analysis_switches_tabs_and_quality_values()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Histogram);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);

        viewModel.Analysis.SelectTab(AnalysisTab.Color);
        Assert.True(viewModel.Analysis.IsColorTab);
        Assert.False(viewModel.Analysis.IsHistogramTab);

        viewModel.Analysis.SelectTab(AnalysisTab.Data);
        Assert.True(viewModel.Analysis.IsDataTab);
        Assert.NotEmpty(viewModel.Analysis.Display.DataSections);

        Assert.InRange(viewModel.Analysis.Display.SharpnessFraction, 0, 1);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Analysis.Display.NoiseText));
    }

    [AvaloniaFact]
    public void Analysis_labels_switch_with_the_language()
    {
        using var fixture = new WindowFixture();
        var viewModel = Assert.IsType<MainWindowViewModel>(fixture.Window.DataContext);
        var before = viewModel.Analysis.Labels.Title;

        fixture.Services.SetLanguage(LocalizationService.ChineseSimplified);

        Assert.NotEqual(before, viewModel.Analysis.Labels.Title);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Analysis.Labels.Title));
    }

    [AvaloniaFact]
    public async Task Fullscreen_hides_and_restores_the_analysis_drawer()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Histogram);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);

        window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.IsFullscreen);
        Assert.True(viewModel.AnalysisVisible);
        Assert.False(viewModel.AnalysisShown);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !viewModel.IsFullscreen);
        Assert.True(viewModel.AnalysisShown);
    }

    [AvaloniaFact]
    public async Task Information_panel_replaces_the_analysis_drawer()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Histogram);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.InfoVisible);
        Assert.True(viewModel.InfoShown);
        Assert.False(viewModel.AnalysisVisible);
    }

    [AvaloniaFact]
    public async Task Eyedropper_samples_can_be_pinned_and_cleared()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Color);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);
        viewModel.Analysis.SetEyedropper(true);
        Assert.True(viewModel.Analysis.ShowNoSampleActive);

        Assert.True(viewModel.TryGetDisplayedPixel(20, 20, out var color, out var alpha));
        viewModel.Analysis.HoverSample(20, 20, color, alpha);
        Assert.True(viewModel.Analysis.HasLiveSample);
        viewModel.Analysis.PinSample(20, 20, color, alpha);

        Assert.Single(viewModel.Analysis.Pins);
        Assert.Equal("1", viewModel.Analysis.Pins[0].IndexText);
        Assert.Equal("1 / 4", viewModel.Analysis.PinnedCountText);

        viewModel.Analysis.PinSample(20, 20, color, alpha);
        Assert.Empty(viewModel.Analysis.Pins);
        Assert.Equal("0 / 4", viewModel.Analysis.PinnedCountText);
    }

    [AvaloniaFact]
    public async Task Analysis_menu_contains_the_tab_items_and_eyedropper()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);
        Dispatcher.UIThread.RunJobs();

        var canvas = window.GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Classes.Contains("canvas"));
        window.MouseDown(new Point(300, 260), MouseButton.Right);
        window.MouseUp(new Point(300, 260), MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        var menu = canvas.ContextMenu;
        Assert.NotNull(menu);
        var analyze = menu!.Items
            .OfType<MenuItem>()
            .FirstOrDefault(item => item.Header as string == "Analyze");
        Assert.NotNull(analyze);
        Assert.Equal(5, analyze!.Items.OfType<MenuItem>().Count());
        Assert.Contains(analyze.Items.OfType<MenuItem>(), item => item.ToggleType == MenuItemToggleType.CheckBox);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public async Task Edit_mode_closes_the_analysis_drawer()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        viewModel.OpenAnalysis(AnalysisTab.Quality);
        await WaitUntilAsync(() => viewModel.Analysis.HasResult);

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
        await WaitUntilAsync(() => viewModel.IsEditing);
        Assert.False(viewModel.AnalysisVisible);
        Assert.False(viewModel.Analysis.IsEyedropperActive);
    }

    [AvaloniaFact]
    public async Task Canvas_receives_pointer_hits_through_the_overlay_layers()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage && window.Bounds.Width > 0);
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

    [AvaloniaFact]
    public async Task Tiff_is_presented_as_an_image()
    {
        using var fixture = new WindowFixture("rgb-lzw.tif");
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => viewModel.HasImage);

        var viewer = window.GetVisualDescendants().OfType<ImageViewer>().First();
        Assert.NotNull(viewer.Source);
        Assert.Equal(80, viewer.SourcePixelWidth);
        Assert.Equal(40, viewer.SourcePixelHeight);
    }

    [AvaloniaFact]
    public async Task Dual_view_presents_two_panes_and_switches_active_pane()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
        {
            var dual = window.GetVisualDescendants().OfType<DualCanvasView>().FirstOrDefault();
            return dual is { IsVisible: true } &&
                   dual.GetVisualDescendants().OfType<ImageViewer>().Count(viewer => viewer.Source is not null) == 2;
        });

        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single();
        Assert.Equal(0, dualView.ActiveIndex);
        Assert.True(dualView.HasAnyImage);
        Assert.Equal(2, dualView.GetVisualDescendants().OfType<ImageViewer>().Count());
        Assert.Single(
            dualView.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dual-active-frame") && border.IsVisible);
        Assert.Equal("a.jpg", Path.GetFileName(dualView.PanePath(0)));
        Assert.Equal("b.jpg", Path.GetFileName(dualView.PanePath(1)));

        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            Path.GetFileName(dualView.PanePath(0)) == "b.jpg" &&
            Path.GetFileName(dualView.PanePath(1)) == "a.jpg" &&
            dualView.ActiveIndex == 0);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            Path.GetFileName(dualView.PanePath(0)) == "a.jpg" &&
            Path.GetFileName(dualView.PanePath(1)) == "b.jpg");

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        await WaitUntilAsync(() => dualView.ActiveIndex == 1);

        var leftPath = dualView.PanePath(0);
        var otherFolder = Path.Combine(Path.GetDirectoryName(leftPath!)!, "other");
        Directory.CreateDirectory(otherFolder);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-2.jpg"),
            Path.Combine(otherFolder, "other.jpg"));
        await dualView.OpenPathAsync(1, otherFolder);
        Assert.Equal(leftPath, dualView.PanePath(0));
        Assert.Equal("other.jpg", Path.GetFileName(dualView.PanePath(1)));

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().All(view => !view.IsVisible) &&
            window.GetVisualDescendants().OfType<ImageViewer>().First().Source is not null);
    }

    [AvaloniaFact]
    public async Task Dual_view_plays_an_animated_gif_in_its_pane()
    {
        using var fixture = new WindowFixture("animated.gif");
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>()
                .Any(dual => dual.IsVisible && dual.PanePath(0)?.EndsWith("animated.gif") == true &&
                             dual.GetVisualDescendants().OfType<ImageViewer>().Any(viewer => viewer.Source is not null)));

        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        var firstViewer = dualView.GetVisualDescendants().OfType<ImageViewer>().First();
        var first = firstViewer.Source;
        Assert.NotNull(first);
        await WaitUntilAsync(() => !ReferenceEquals(first, firstViewer.Source), 3_000);
    }

    [AvaloniaFact]
    public async Task Dual_view_button_is_available_in_single_view()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(candidate => Equals(ToolTip.GetTip(candidate), "Dual view (D)"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
    }

    [AvaloniaFact]
    public async Task Dual_view_shows_an_empty_right_canvas_when_the_folder_has_one_image()
    {
        using var fixture = new WindowFixture(includeSecondImage: false);
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));

        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        // The right canvas starts empty and its card appears once the initial
        // dual-view load finishes, which is not the same moment the view shows.
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("dual-empty-card") && border.IsVisible));
        Assert.Null(dualView.PanePath(1));
        Assert.Contains(
            dualView.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dual-empty-card") && border.IsVisible);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("dual-filmstrip") && border.IsVisible));

        Assert.DoesNotContain(
            dualView.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dual-film-empty") && border.IsVisible);
        var bHalf = dualView.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("dual-film-half") && border.Classes.Contains("b"));
        Assert.Contains(
            bHalf.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dual-film-badge") && border.Classes.Contains("b"));
        Assert.DoesNotContain(
            bHalf.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("dual-film-empty-button") && button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Dual_panels_follow_the_active_canvas()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        // DualCanvasView is visible before InitializeAsync finishes; wait for both
        // panes and the initial active state before exercising focus and Tab.
        await WaitUntilAsync(() =>
            Path.GetFileName(dualView.PanePath(0)) == "a.jpg" &&
            Path.GetFileName(dualView.PanePath(1)) == "b.jpg" &&
            dualView.ActiveIndex == 0);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("dual-filmstrip") && border.IsVisible));
        var halves = dualView.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("dual-film-half"))
            .ToList();
        Assert.Equal(2, halves.Count);
        Assert.Single(halves, half => half.Classes.Contains("active"));

        dualView.Focus();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        await WaitUntilAsync(() => dualView.ActiveIndex == 1);
        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("info-overlay") && border.IsVisible));

        Assert.DoesNotContain(
            dualView.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dual-filmstrip") && border.IsVisible);
        Assert.Contains(
            dualView.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("dual-tab") &&
                      button.Classes.Contains("b") &&
                      button.Classes.Contains("active"));
    }

    [AvaloniaFact]
    public async Task Filmstrip_is_disabled_when_both_canvases_are_empty()
    {
        using var fixture = new WindowFixture(openInitialImage: false);
        var window = fixture.Window;
        window.Show();
        await WaitUntilAsync(() => window.IsVisible);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        Assert.Null(dualView.PanePath(0));
        Assert.Null(dualView.PanePath(1));

        var filmstripButton = dualView.GetVisualDescendants().OfType<Button>()
            .First(button => Equals(ToolTip.GetTip(button), "Filmstrip"));
        Assert.False(filmstripButton.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
        await Task.Delay(100);
        Assert.DoesNotContain(
            dualView.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dual-filmstrip") && border.IsVisible);
    }

    [AvaloniaFact]
    public async Task Per_pane_navigation_buttons_target_their_canvas()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        // IsVisible is set before DualCanvasView.InitializeAsync finishes; wait for the
        // first pane's initial image so the next-pane button has something to page.
        await WaitUntilAsync(() => Path.GetFileName(dualView.PanePath(0)) == "a.jpg");

        var buttons = dualView.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("dual-pane-nav-button"))
            .ToList();
        Assert.Equal(4, buttons.Count);
        var aNext = buttons.Single(button =>
            button.Classes.Contains("a") && button.Classes.Contains("next"));
        aNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() => Path.GetFileName(dualView.PanePath(0)) == "b.jpg");
        Assert.Equal(0, dualView.ActiveIndex);
    }

    [AvaloniaFact]
    public async Task Right_canvas_header_stays_clear_of_the_window_controls_at_the_default_width()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        window.Width = 1240;
        window.Height = 860;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        var header = dualView.GetVisualDescendants().OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("dual-pane-header") && panel.Classes.Contains("b"));
        // Wait for the page counter to be populated so the header is measured at
        // its final width instead of the empty placeholder width.
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.Text == "02 / 02"));
        dualView.UpdateLayout();

        var headerRight = header.TranslatePoint(new Point(header.Bounds.Width, 0), window);
        Assert.NotNull(headerRight);
        var maximize = window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Tag, "L.Maximize"));
        if (OperatingSystem.IsWindows())
        {
            var maximizeLeft = maximize.TranslatePoint(new Point(0, 0), window);
            Assert.NotNull(maximizeLeft);
            Assert.True(headerRight.Value.X <= maximizeLeft.Value.X,
                $"B header ends at {headerRight.Value.X}, Maximize starts at {maximizeLeft.Value.X}");
        }
        else
        {
            // Only Windows draws the caption row inside the client area; other
            // platforms use native decorations, so the header cannot reach them.
            Assert.False(maximize.IsEffectivelyVisible);
            Assert.True(headerRight.Value.X <= window.Bounds.Width,
                $"B header ends at {headerRight.Value.X}, window ends at {window.Bounds.Width}");
        }

        Assert.Contains(
            dualView.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "02 / 02" && text.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task File_chip_is_capped_and_header_tooltips_show_full_names()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        window.Width = 1920;
        window.Height = 1080;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);
        var fileChip = dualView.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("dual-file-chip") && border.Classes.Contains("b"));
        var folderChip = dualView.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("dual-folder-chip") && border.Classes.Contains("b"));
        // The tooltips are assigned when the header applies its responsive layout,
        // which can run after the dual view first becomes visible.
        await WaitUntilAsync(() => ToolTip.GetTip(fileChip) is not null);
        fileChip.UpdateLayout();

        Assert.True(fileChip.Bounds.Width <= 341,
            $"File chip should cap at 340 px, got {fileChip.Bounds.Width}");
        Assert.Equal("b.jpg", ToolTip.GetTip(fileChip));
        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(folderChip) as string));
    }

    [AvaloniaFact]
    public async Task Pane_menu_primary_item_is_not_hovered_by_default_and_hovers_on_pointer()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);

        window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("dual-menu") && border.IsVisible));
        var primary = dualView.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("dual-menu-item") && button.Classes.Contains("primary"));
        Assert.DoesNotContain("hover", primary.Classes);

        var point = primary.TranslatePoint(
            new Point(primary.Bounds.Width / 2, primary.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseMove(point.Value);
        await WaitUntilAsync(() => primary.Classes.Contains("hover"));
        Assert.True(primary.IsPointerOver);
        Assert.Equal(Color.Parse("#332E6F63"),
            Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
    }

    [AvaloniaFact]
    public async Task ShiftF10_opens_and_escape_closes_the_active_pane_menu()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);

        window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
        await WaitUntilAsync(() => dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("dual-menu") && border.IsVisible));

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !dualView.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Classes.Contains("dual-menu") && border.IsVisible));
    }

    [AvaloniaFact]
    public async Task Escape_exits_fullscreen_before_leaving_dual_view()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
        window.Show();
        await WaitUntilAsync(() => window.IsVisible && viewModel.HasImage);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        await WaitUntilAsync(() =>
            window.GetVisualDescendants().OfType<DualCanvasView>().Any(view => view.IsVisible));

        window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
        await WaitUntilAsync(() => viewModel.IsFullscreen);
        var dualView = window.GetVisualDescendants().OfType<DualCanvasView>().Single(view => view.IsVisible);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !viewModel.IsFullscreen);
        Assert.True(dualView.IsVisible);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !dualView.IsVisible);
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

        public WindowFixture(string? initialAsset = null, bool includeSecondImage = true,
            bool openInitialImage = true)
        {
            _root = Path.Combine(Path.GetTempPath(), "airglance-window-" + Guid.NewGuid().ToString("N"));
            var images = Path.Combine(_root, "images");
            Directory.CreateDirectory(images);
            Directory.CreateDirectory(Path.Combine(_root, "settings"));
            Directory.CreateDirectory(Path.Combine(_root, "cache"));

            var names = includeSecondImage ? new[] { "a.jpg", "b.jpg" } : new[] { "a.jpg" };
            foreach (var name in names)
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
            var options = CommandLineOptions.Parse(openInitialImage
                ? [Path.Combine(images, initialAsset ?? "a.jpg")]
                : []);
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
