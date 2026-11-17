using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirGlance.App.ViewModels;

/// <summary>One entry in the filmstrip. The thumbnail arrives from a background queue.</summary>
public partial class ImageItemViewModel : ObservableObject
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
}
