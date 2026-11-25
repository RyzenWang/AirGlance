using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirGlance.App.ViewModels;

/// <summary>One entry in the filmstrip. The thumbnail arrives from a background queue.</summary>
public partial class ImageItemViewModel : ObservableObject, IDisposable
{
    public ImageItemViewModel(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }

    public string FileName { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _thumbnailFailed;

    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// Releases the thumbnail. The filmstrip keeps a native bitmap per entry, and a
    /// folder change used to drop up to a thousand of them to the finalizer.
    /// </summary>
    public void Dispose()
    {
        var thumbnail = Thumbnail;
        Thumbnail = null;
        thumbnail?.Dispose();
    }
}
