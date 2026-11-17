using AirGlance.Core.Files;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "airglance-scan-" + Guid.NewGuid().ToString("N"));

    public FolderScannerTests()
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
    public void Only_supported_files_are_listed_in_natural_order()
    {
        foreach (var name in new[] { "img10.jpg", "img2.JPG", "img1.png", "notes.txt", "img3.webp", "img02.jpg" })
        {
            File.WriteAllBytes(Path.Combine(_folder, name), [0]);
        }

        var files = FolderScanner.EnumerateImages(_folder);

        Assert.Equal(
            new[] { "img1.png", "img2.JPG", "img02.jpg", "img3.webp", "img10.jpg" },
            files.Select(Path.GetFileName));
    }

    [Fact]
    public void Descending_order_reverses_the_list()
    {
        foreach (var name in new[] { "img1.jpg", "img2.jpg", "img10.jpg" })
        {
            File.WriteAllBytes(Path.Combine(_folder, name), [0]);
        }

        var files = FolderScanner.EnumerateImages(_folder, descending: true);

        Assert.Equal(new[] { "img10.jpg", "img2.jpg", "img1.jpg" }, files.Select(Path.GetFileName));
    }

    [Fact]
    public void Hidden_files_are_skipped()
    {
        var visible = Path.Combine(_folder, "visible.jpg");
        var hidden = Path.Combine(_folder, "hidden.jpg");
        File.WriteAllBytes(visible, [0]);
        File.WriteAllBytes(hidden, [0]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var files = FolderScanner.EnumerateImages(_folder);

        Assert.Equal([visible], files);
    }

    [Fact]
    public void Missing_folder_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => FolderScanner.EnumerateImages(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void Empty_folder_returns_empty_list()
    {
        Assert.Empty(FolderScanner.EnumerateImages(_folder));
    }
}

public sealed class ImageSourceResolverTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "airglance-source-" + Guid.NewGuid().ToString("N"));

    public ImageSourceResolverTests()
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
    public void Opening_a_file_selects_it_in_its_folder()
    {
        var first = Path.Combine(_folder, "a.jpg");
        var second = Path.Combine(_folder, "b.jpg");
        File.WriteAllBytes(first, [0]);
        File.WriteAllBytes(second, [0]);

        var source = ImageSourceResolver.Resolve(second);

        Assert.Equal(_folder, source.FolderPath);
        Assert.Equal(1, source.Index);
        Assert.Equal(second, source.CurrentPath);
    }

    [Fact]
    public void Opening_a_folder_starts_at_the_first_image()
    {
        File.WriteAllBytes(Path.Combine(_folder, "img2.jpg"), [0]);
        File.WriteAllBytes(Path.Combine(_folder, "img10.jpg"), [0]);

        var source = ImageSourceResolver.Resolve(_folder);

        Assert.Equal(0, source.Index);
        Assert.EndsWith("img2.jpg", source.CurrentPath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directly_opened_hidden_file_still_opens_alone()
    {
        var hidden = Path.Combine(_folder, "hidden.jpg");
        File.WriteAllBytes(hidden, [0]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var source = ImageSourceResolver.Resolve(hidden);

        Assert.Equal([hidden], source.Files);
    }

    [Fact]
    public void Missing_path_throws()
    {
        Assert.Throws<FileNotFoundException>(() => ImageSourceResolver.Resolve(Path.Combine(_folder, "nope.jpg")));
    }
}
