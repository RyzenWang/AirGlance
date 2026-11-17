using AirGlance.Core.Storage;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "airglance-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void Defaults_are_normalized()
    {
        var service = new SettingsService(_folder);
        var settings = service.Load();

        Assert.Contains(settings.Language, new[] { "en", "zh-CN" });
        Assert.Equal("System", settings.Theme);
        Assert.Equal("name", settings.Sort);
        Assert.Equal(5, settings.SlideshowSeconds);
        Assert.True(settings.RememberZoom);
        Assert.True(settings.Checkerboard);
    }

    [Fact]
    public void Settings_roundtrip()
    {
        var service = new SettingsService(_folder);
        var settings = service.Load();
        settings.Language = "zh-CN";
        settings.Theme = "Dark";
        settings.Sort = "name-descending";
        settings.SlideshowSeconds = 10;
        settings.RememberZoom = false;
        settings.Checkerboard = false;
        settings.ZoomMode = "actual";
        settings.ZoomFactor = 2.5;

        service.Save(settings);
        var loaded = new SettingsService(_folder).Load();

        Assert.Equal("zh-CN", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("name-descending", loaded.Sort);
        Assert.Equal(10, loaded.SlideshowSeconds);
        Assert.False(loaded.RememberZoom);
        Assert.False(loaded.Checkerboard);
        Assert.Equal("actual", loaded.ZoomMode);
        Assert.Equal(2.5, loaded.ZoomFactor, 6);
    }

    [Fact]
    public void Invalid_values_are_repaired()
    {
        var service = new SettingsService(_folder);
        var settings = new AppSettings
        {
            Language = "fr",
            Theme = "Sepia",
            Sort = "size",
            SlideshowSeconds = 7,
            ZoomMode = "teleport",
            ZoomFactor = double.NaN,
            WindowWidth = 10,
            WindowHeight = double.NaN
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal("en", loaded.Language);
        Assert.Equal("System", loaded.Theme);
        Assert.Equal("name", loaded.Sort);
        Assert.Equal(5, loaded.SlideshowSeconds);
        Assert.Equal("fit", loaded.ZoomMode);
        Assert.Equal(1.0, loaded.ZoomFactor);
        Assert.Equal(1240, loaded.WindowWidth);
        Assert.Equal(860, loaded.WindowHeight);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ this is not json");

        var settings = new SettingsService(_folder).Load();

        Assert.Equal("System", settings.Theme);
        Assert.Equal(5, settings.SlideshowSeconds);
    }

    [Fact]
    public void Save_keeps_a_backup_and_leaves_no_temporary_file()
    {
        var service = new SettingsService(_folder);
        service.Save(new AppSettings { Theme = "Dark" });
        service.Save(new AppSettings { Theme = "Light" });

        Assert.True(File.Exists(service.SettingsPath));
        Assert.True(File.Exists(service.BackupPath));
        Assert.False(File.Exists(service.SettingsPath + ".tmp"));
    }

    [Fact]
    public void Broken_primary_file_uses_the_backup()
    {
        var service = new SettingsService(_folder);
        service.Save(new AppSettings { Theme = "Dark" });
        service.Save(new AppSettings { Theme = "Light" });

        Assert.True(File.Exists(service.BackupPath), "the second save should keep the previous file as a backup");
        Assert.Contains("\"theme\": \"Dark\"", File.ReadAllText(service.BackupPath), StringComparison.Ordinal);

        File.WriteAllText(service.SettingsPath, "{ broken");

        var loaded = service.Load();

        Assert.Equal("Dark", loaded.Theme);
    }

    [Fact]
    public async Task Async_load_reads_the_same_file()
    {
        var service = new SettingsService(_folder);
        service.Save(new AppSettings { Language = "zh-CN" });

        var loaded = await service.LoadAsync();

        Assert.Equal("zh-CN", loaded.Language);
    }

    [Fact]
    public async Task Async_load_of_missing_file_returns_defaults()
    {
        var service = new SettingsService(Path.Combine(_folder, "missing"));

        var loaded = await service.LoadAsync();

        Assert.Equal("System", loaded.Theme);
    }
}
