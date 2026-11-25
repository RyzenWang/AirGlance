using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.Core.Imaging;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airglance-vm-" + Guid.NewGuid().ToString("N"));
    private readonly string _images;
    private readonly AppServices _services;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _images = Path.Combine(_root, "images");
        Directory.CreateDirectory(_images);
        Directory.CreateDirectory(Path.Combine(_root, "settings"));
        Directory.CreateDirectory(Path.Combine(_root, "cache"));

        foreach (var name in new[] { "a.jpg", "b.png", "c.jpg" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg"), Path.Combine(_images, name));
        }

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Assets", "exif-full.jpg"),
            Path.Combine(_images, "d.jpg"));

        _services = new AppServices(Path.Combine(_root, "settings"), Path.Combine(_root, "cache"));
        _services.SetLanguage(LocalizationService.English);
        _viewModel = new MainWindowViewModel(_services);
    }

    public void Dispose()
    {
        _viewModel.Dispose();
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
    public async Task Opening_a_file_selects_it_and_lists_the_folder()
    {
        await _viewModel.OpenPathAsync(Path.Combine(_images, "b.png"));

        Assert.True(_viewModel.HasImage);
        Assert.Equal(4, _viewModel.Images.Count);
        Assert.Equal(1, _viewModel.CurrentIndex);
        Assert.Equal("b.png", _viewModel.TitleFileName);
        Assert.Equal("80 × 40", _viewModel.StatusDimensions);
        Assert.Equal("2/4", _viewModel.StatusPosition);
        Assert.NotNull(_viewModel.Metadata);
        Assert.Equal(80, _viewModel.Metadata!.DisplayWidth);
    }

    [AvaloniaFact]
    public async Task Next_and_previous_wrap_with_a_status_hint()
    {
        await _viewModel.OpenPathAsync(Path.Combine(_images, "a.jpg"));

        await _viewModel.PreviousAsync();
        Assert.Equal(3, _viewModel.CurrentIndex);
        Assert.Equal("Back to the last image.", _viewModel.TransientMessage);

        await _viewModel.NextAsync();
        Assert.Equal(0, _viewModel.CurrentIndex);
        Assert.Equal("Back to the first image.", _viewModel.TransientMessage);
    }

    [AvaloniaFact]
    public async Task Rotation_swaps_status_dimensions_without_touching_the_file()
    {
        var path = Path.Combine(_images, "a.jpg");
        var before = File.ReadAllBytes(path);

        await _viewModel.OpenPathAsync(path);
        await _viewModel.RotateClockwiseAsync();

        Assert.Equal(1, _viewModel.CurrentEdit.QuarterTurns);
        Assert.Equal("40 × 80", _viewModel.StatusDimensions);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task Info_rows_follow_the_language()
    {
        await _viewModel.OpenPathAsync(Path.Combine(_images, "d.jpg"));

        var english = _viewModel.InfoSections.SelectMany(section => section.Rows).Select(row => row.Label).ToList();
        Assert.Contains("Camera", english);
        Assert.Contains("AW 35mm f/1.8", _viewModel.InfoSections.SelectMany(section => section.Rows).Select(row => row.Value));

        _services.SetLanguage(LocalizationService.ChineseSimplified);
        var chinese = _viewModel.InfoSections.SelectMany(section => section.Rows).Select(row => row.Label).ToList();

        Assert.Contains("相机", chinese);
        Assert.DoesNotContain("Camera", chinese);
    }

    [AvaloniaFact]
    public async Task An_unsupported_drop_shows_the_format_message()
    {
        var path = Path.Combine(_root, "photo.heic");
        File.WriteAllBytes(path, [1, 2, 3]);

        await _viewModel.OpenDroppedPathAsync(path);

        Assert.True(_viewModel.HasError);
        Assert.True(_viewModel.OverlayVisible);
        Assert.False(_viewModel.HasImage);
        Assert.Contains(".heic", _viewModel.OverlayMessage);
    }

    [AvaloniaFact]
    public async Task Panels_and_settings_toggle()
    {
        await _viewModel.OpenPathAsync(Path.Combine(_images, "a.jpg"));

        _viewModel.ToggleInfo();
        _viewModel.ToggleFilmstrip();
        Assert.False(_viewModel.InfoVisible);
        Assert.True(_viewModel.FilmstripVisible);

        _services.Settings.Checkerboard = false;
        _services.SaveSettings();
        Assert.False(_viewModel.CheckerboardEnabled);

        _viewModel.SetFullscreen(true);
        Assert.False(_viewModel.FilmstripShown);
        Assert.False(_viewModel.InfoShown);

        _viewModel.SetFullscreen(false);
        Assert.True(_viewModel.FilmstripShown);
        Assert.False(_viewModel.InfoShown);
    }

    [AvaloniaFact]
    public async Task Edit_session_starts_and_cancels_non_destructively()
    {
        await _viewModel.OpenPathAsync(Path.Combine(_images, "a.jpg"));

        Assert.True(_viewModel.CanEdit);
        Assert.True(_viewModel.BeginEdit());
        Assert.True(_viewModel.IsEditing);

        _viewModel.SetCrop(new CropRegion(0.2, 0.2, 0.6, 0.6));
        await _viewModel.RotateClockwiseAsync();
        Assert.NotEqual(EditTransform.None, _viewModel.CurrentEdit);

        await _viewModel.CancelEditAsync();

        Assert.False(_viewModel.IsEditing);
        Assert.Equal(EditTransform.None, _viewModel.CurrentEdit);
        Assert.True(_viewModel.Crop.IsFull);
    }

    [AvaloniaFact]
    public async Task Thumbnails_arrive_and_are_released_with_the_folder()
    {
        await _viewModel.OpenPathAsync(Path.Combine(_images, "a.jpg"));

        var items = _viewModel.Images.ToArray();
        Assert.Equal(4, items.Length);

        // The bitmap is built on the thumbnail worker and only handed over on the UI
        // thread, so this also covers that decoding off the dispatcher produces a
        // usable image.
        await WaitUntilAsync(() => items.All(item => item.Thumbnail is not null));
        foreach (var item in items)
        {
            Assert.NotNull(item.Thumbnail);
            Assert.True(item.Thumbnail!.PixelSize.Width > 0);
        }

        // Replacing the folder must retire the entries: the old thumbnails are native
        // bitmaps, and they used to be dropped to the finalizer instead.
        await _viewModel.OpenPathAsync(Path.Combine(_images, "b.png"));
        Assert.All(items, item => Assert.Null(item.Thumbnail));
        Assert.Equal(4, _viewModel.Images.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for the thumbnail queue.");
    }

    [AvaloniaFact]
    public async Task A_corrupt_file_reports_corrupt()
    {
        var path = Path.Combine(_images, "broken.jpg");
        File.WriteAllText(path, "not a jpeg");

        await _viewModel.OpenPathAsync(path);

        Assert.True(_viewModel.HasError);
        Assert.Contains("damaged", _viewModel.OverlayMessage);
    }

    [AvaloniaFact]
    public async Task Empty_folder_shows_the_folder_empty_state()
    {
        var folder = Path.Combine(_root, "empty");
        Directory.CreateDirectory(folder);

        await _viewModel.OpenPathAsync(folder);

        Assert.False(_viewModel.HasImage);
        Assert.True(_viewModel.OverlayVisible);
        Assert.Equal("No images in this folder.", _viewModel.OverlayTitle);
    }
}
