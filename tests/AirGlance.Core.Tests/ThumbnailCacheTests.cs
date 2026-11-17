using AirGlance.Core.Imaging;
using AirGlance.Core.Storage;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class ThumbnailCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "airglance-cache-" + Guid.NewGuid().ToString("N"));
    private readonly string _image = Path.Combine(Path.GetTempPath(), "airglance-image-" + Guid.NewGuid().ToString("N") + ".jpg");

    public ThumbnailCacheTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(_image, [1, 2, 3]);
    }

    public void Dispose()
    {
        foreach (var path in new[] { _folder, Path.GetDirectoryName(_image)! })
        {
            try
            {
                if (path == Path.GetDirectoryName(_image))
                {
                    File.Delete(_image);
                }
                else
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private static byte[] SamplePng() =>
        ImageExporter.Encode(
            new DecodedBitmap(4, 4, new byte[4 * 4 * 4], 4, 4, 1.0),
            ExportFormat.Png);

    [Fact]
    public void Written_entries_are_read_back_for_the_same_file_version()
    {
        var cache = new ThumbnailCache(_folder);
        var timestamp = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var png = SamplePng();

        cache.Write(_image, timestamp, 3, png);

        Assert.True(cache.TryRead(_image, timestamp, 3, out var read));
        Assert.Equal(png, read);
        Assert.False(cache.TryRead(_image, timestamp, 4, out _));
        Assert.False(cache.TryRead(_image, timestamp.AddMinutes(1), 3, out _));
    }

    [Fact]
    public void Cleanup_removes_only_old_entries()
    {
        var cache = new ThumbnailCache(_folder);
        var timestamp = DateTime.UtcNow;
        cache.Write(_image, timestamp, 3, SamplePng());
        var entry = Assert.Single(Directory.EnumerateFiles(_folder));
        File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddDays(-31));

        var deleted = cache.Cleanup(TimeSpan.FromDays(30));

        Assert.Equal(1, deleted);
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }

    [Fact]
    public void Clear_and_size_work()
    {
        var cache = new ThumbnailCache(_folder);
        cache.Write(_image, DateTime.UtcNow, 3, SamplePng());
        cache.Write(_image + ".second", DateTime.UtcNow, 3, SamplePng());

        Assert.True(cache.GetSizeBytes() > 0);
        Assert.Equal(2, cache.Clear());
        Assert.Equal(0, cache.GetSizeBytes());
    }

    [Fact]
    public void Missing_cache_directory_is_not_an_error()
    {
        var cache = new ThumbnailCache(Path.Combine(_folder, "missing"));

        Assert.False(cache.TryRead(_image, DateTime.UtcNow, 3, out _));
        Assert.Equal(0, cache.Cleanup(TimeSpan.FromDays(30)));
        Assert.Equal(0, cache.GetSizeBytes());
        Assert.Equal(0, cache.Clear());
    }
}
