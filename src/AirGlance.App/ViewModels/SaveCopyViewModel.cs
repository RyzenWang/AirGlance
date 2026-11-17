using AirGlance.App.Services;
using AirGlance.Core.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirGlance.App.ViewModels;

/// <summary>
/// "Save a copy": choose a format and a path, see the exact encoded size (the encoder
/// runs in the background), and write a new file. The original path is never opened
/// for writing.
/// </summary>
public sealed partial class SaveCopyViewModel : ObservableObject, IDisposable
{
    private readonly Func<ExportFormat, CancellationToken, Task<byte[]>> _encode;
    private readonly LocalizationService _localization;
    private readonly string _sourcePath;
    private CancellationTokenSource? _estimateCts;
    private byte[]? _encoded;
    private ExportFormat _encodedFormat;
    private bool _initializing = true;
    private bool _disposed;

    public SaveCopyViewModel(
        string sourcePath,
        LocalizationService localization,
        Func<ExportFormat, CancellationToken, Task<byte[]>> encode)
    {
        _sourcePath = sourcePath;
        _localization = localization;
        _encode = encode;
        _filePath = ImageExporter.GetDefaultCopyPath(sourcePath, ExportFormat.Jpeg);
    }

    public string SourcePath => _sourcePath;

    public IReadOnlyList<string> FormatNames { get; } = ["JPEG", "PNG", "WebP"];

    public ExportFormat SelectedFormat => ExportFormats.All[Math.Clamp(FormatIndex, 0, ExportFormats.All.Count - 1)];

    public event EventHandler<string>? Saved;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private int _formatIndex;

    [ObservableProperty]
    private string _estimateText = string.Empty;

    [ObservableProperty]
    private bool _isEstimating;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    public string EstimatedSizeLabel => _localization.T("L.EstimatedSize");

    public string BrowseTitle => _localization.T("L.Browse");

    public async Task InitializeAsync()
    {
        _initializing = false;
        await EstimateAsync();
    }

    public void SetFolder(string folder)
    {
        try
        {
            FilePath = Path.Combine(folder, Path.GetFileName(FilePath));
        }
        catch
        {
            // Ignore a folder that cannot be combined; the save validation reports it.
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        var format = SelectedFormat;
        FilePath = Path.ChangeExtension(FilePath, format.Extension());

        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ErrorMessage = _localization.T("L.SaveCopyError");
            return;
        }

        IsSaving = true;
        ErrorMessage = null;
        _estimateCts?.Cancel();

        try
        {
            var bytes = _encoded is not null && _encodedFormat == format
                ? _encoded
                : await _encode(format, CancellationToken.None);

            var written = await Task.Run(() => ImageExporter.WriteNewFile(FilePath, bytes));
            Saved?.Invoke(this, written);
        }
        catch
        {
            ErrorMessage = _localization.T("L.SaveFailed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    partial void OnFormatIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }

        var format = SelectedFormat;
        try
        {
            FilePath = Path.ChangeExtension(FilePath, format.Extension());
        }
        catch
        {
            // Leave the typed path alone; validation happens on save.
        }

        _encoded = null;
        _ = EstimateAsync();
    }

    private async Task EstimateAsync()
    {
        var format = SelectedFormat;
        _estimateCts?.Cancel();
        _estimateCts?.Dispose();
        _estimateCts = new CancellationTokenSource();
        var cancellationToken = _estimateCts.Token;

        IsEstimating = true;
        EstimateText = _localization.T("L.Estimating");

        try
        {
            var bytes = await _encode(format, cancellationToken);
            if (cancellationToken.IsCancellationRequested || format != SelectedFormat)
            {
                return;
            }

            _encoded = bytes;
            _encodedFormat = format;
            EstimateText = _localization.Format("L.EstimateFormat", FormatBytes(bytes.Length));
        }
        catch (OperationCanceledException)
        {
            // Superseded by another format.
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                EstimateText = _localization.T("L.EstimateFailed");
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsEstimating = false;
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / 1024.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _estimateCts?.Cancel();
        _estimateCts?.Dispose();
        _estimateCts = null;
    }
}

