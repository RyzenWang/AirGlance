using System.Globalization;
using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.Core.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirGlance.App.ViewModels;

public enum AnalysisTab
{
    Histogram,
    Color,
    Quality,
    Data
}

public sealed record AnalysisRequest(
    string Path,
    DecodedBitmap Raw,
    DecodedBitmap Display,
    ImageMetadata Metadata,
    int FrameCount,
    double DurationSeconds,
    int LoopCount);

public sealed class AnalysisSampleView
{
    public required IBrush Brush { get; init; }

    public required string Hex { get; init; }

    public required string PositionText { get; init; }

    public required string RgbText { get; init; }

    public required string HslText { get; init; }

    public required string LabText { get; init; }

    public required string AlphaText { get; init; }

    public string IndexText { get; init; } = string.Empty;

    public int X { get; init; }

    public int Y { get; init; }
}

/// <summary>
/// Owns the analysis result, formatting and the eyedropper samples for one image.
/// Pixel analysis runs in the background; this class only keeps the result, never a
/// pixel buffer.
/// </summary>
public sealed partial class AnalysisViewModel : ObservableObject, IDisposable
{
    private const int PinLimit = 4;
    private readonly LocalizationService _localization;
    private readonly Dictionary<string, CachedAnalysis> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private CancellationTokenSource? _cts;
    private ImageAnalysisResult? _result;
    private ImageMetadata? _metadata;
    private string? _key;
    private bool _isOriginal;
    private int _version;
    private bool _disposed;
    private bool _errorIsFailure;

    [ObservableProperty]
    private AnalysisTab _activeTab = AnalysisTab.Histogram;

    [ObservableProperty]
    private HistogramChartMode _chartMode = HistogramChartMode.Luma;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isEyedropperActive;

    [ObservableProperty]
    private AnalysisDisplay _display;

    [ObservableProperty]
    private IReadOnlyList<AnalysisSampleView> _pins = [];

    [ObservableProperty]
    private AnalysisSampleView? _liveSample;

    [ObservableProperty]
    private bool _hasLiveSample;

    [ObservableProperty]
    private string _pinnedCountText = "0 / 4";

    [ObservableProperty]
    private string _resolutionText = string.Empty;

    public AnalysisViewModel(LocalizationService localization)
    {
        _localization = localization;
        Labels = new AnalysisLabels(localization);
        Display = AnalysisDisplay.Empty(Labels);
        localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? CloseRequested;

    public event EventHandler? SamplesChanged;

    public AnalysisLabels Labels { get; private set; }

    public bool IsHistogramTab => ActiveTab == AnalysisTab.Histogram;

    public bool IsColorTab => ActiveTab == AnalysisTab.Color;

    public bool IsQualityTab => ActiveTab == AnalysisTab.Quality;

    public bool IsDataTab => ActiveTab == AnalysisTab.Data;

    public bool IsLumaMode => ChartMode == HistogramChartMode.Luma;

    public bool IsRgbMode => ChartMode == HistogramChartMode.Rgb;

    public bool IsHueMode => ChartMode == HistogramChartMode.Hue;

    public bool IsSaturationMode => ChartMode == HistogramChartMode.Saturation;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool ShowNoSampleActive => IsEyedropperActive && !HasLiveSample;

    public bool ShowNoSampleInactive => !IsEyedropperActive && !HasLiveSample;

    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void SelectTab(AnalysisTab tab)
    {
        ActiveTab = tab;
        if (tab == AnalysisTab.Color)
        {
            // Nothing else: the eyedropper stays a separate mode.
        }
    }

    public void SelectMode(HistogramChartMode mode) => ChartMode = mode;

    public void SetEyedropper(bool active)
    {
        IsEyedropperActive = active;
        if (!active)
        {
            ClearLive();
        }
    }

    public void ClearSamples()
    {
        Pins = [];
        PinnedCountText = string.Format(
            CultureInfo.InvariantCulture,
            Labels.PinnedCountFormat,
            0);
        ClearLive();
        SamplesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HoverSample(int x, int y, RgbColor color, byte alpha)
    {
        if (!IsEyedropperActive)
        {
            return;
        }

        LiveSample = CreateSample(x, y, color, alpha, null);
        HasLiveSample = true;
    }

    public void PinSample(int x, int y, RgbColor color, byte alpha)
    {
        if (!IsEyedropperActive)
        {
            return;
        }

        var existing = Pins.FirstOrDefault(pin => pin.X == x && pin.Y == y);
        if (existing is not null)
        {
            Pins = Pins.Where(pin => !ReferenceEquals(pin, existing)).ToArray();
        }
        else if (Pins.Count < PinLimit)
        {
            var nextIndex = Enumerable.Range(1, PinLimit)
                .First(index => Pins.All(pin => pin.IndexText != index.ToString(CultureInfo.InvariantCulture)));
            var updated = Pins.ToList();
            updated.Add(CreateSample(x, y, color, alpha, nextIndex));
            Pins = updated;
        }
        else
        {
            return;
        }

        PinnedCountText = string.Format(
            CultureInfo.InvariantCulture,
            Labels.PinnedCountFormat,
            Pins.Count);
        SamplesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadAsync(AnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _metadata = request.Metadata;

        var key = FormattableString.Invariant(
            $"{request.Path}|{request.Metadata.FileSizeBytes}|{request.Metadata.LastWriteTime.UtcTicks}|{request.FrameCount}");
        if (string.Equals(key, _key, StringComparison.Ordinal) && (HasResult || IsAnalyzing))
        {
            return;
        }

        if (_cache.TryGetValue(key, out var cached))
        {
            Cancel();
            ClearSamples();
            _key = key;
            _result = cached.Result;
            _isOriginal = cached.IsOriginal;
            Apply(cached.Result, FormatResolutionNote(cached.IsOriginal, cached.Result));
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var version = ++_version;

        _key = key;
        _result = null;
        HasResult = false;
        IsAnalyzing = true;
        ErrorText = null;
        _errorIsFailure = false;
        ResolutionText = string.Empty;
        Display = AnalysisDisplay.Empty(Labels);
        ClearSamples();

        try
        {
            var metadata = request.Metadata;
            var result = await Task.Run(
                () =>
                {
                    var header = ImageHeaderReader.Read(request.Path);
                    var context = new ImageAnalysisContext(
                        metadata.FileSizeBytes,
                        header.Jpeg,
                        request.FrameCount,
                        request.DurationSeconds,
                        request.LoopCount);
                    return ImageAnalyzer.Analyze(request.Raw, context, token);
                },
                token);

            token.ThrowIfCancellationRequested();
            if (version != _version)
            {
                return;
            }

            _result = result;
            var isOriginal = request.Raw.IsFullResolution &&
                             result.Width >= metadata.DisplayWidth &&
                             result.Height >= metadata.DisplayHeight;
            _isOriginal = isOriginal;
            var note = FormatResolutionNote(isOriginal, result);

            _cache[key] = new CachedAnalysis(result, isOriginal);
            _cacheOrder.Enqueue(key);
            while (_cacheOrder.Count > 4)
            {
                var oldest = _cacheOrder.Dequeue();
                if (!string.Equals(oldest, _key, StringComparison.Ordinal))
                {
                    _cache.Remove(oldest);
                }
            }

            Apply(result, note);
        }
        catch (OperationCanceledException)
        {
            // Navigation or closing the drawer superseded this run.
        }
        catch
        {
            if (version == _version)
            {
                HasResult = false;
                ErrorText = Labels.Failed;
                _errorIsFailure = true;
            }
        }
        finally
        {
            if (version == _version)
            {
                IsAnalyzing = false;
            }
        }
    }

    public void SetUnavailable()
    {
        Cancel();
        _key = null;
        _result = null;
        _metadata = null;
        _isOriginal = false;
        IsAnalyzing = false;
        HasResult = false;
        ResolutionText = string.Empty;
        ErrorText = Labels.Unavailable;
        _errorIsFailure = false;
        Display = AnalysisDisplay.Empty(Labels);
        ClearSamples();
    }

    public void Cancel()
    {
        _version++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsAnalyzing = false;
    }

    public void Relocalize()
    {
        Labels = new AnalysisLabels(_localization);
        OnPropertyChanged(nameof(Labels));

        if (_result is not null && _metadata is not null)
        {
            var note = FormatResolutionNote(_isOriginal, _result);
            ResolutionText = note;
            Display = AnalysisDisplay.Build(
                _result,
                _metadata,
                Labels,
                _localization.Culture,
                note);
        }
        else if (!HasResult && ErrorText is not null)
        {
            ErrorText = _errorIsFailure ? Labels.Failed : Labels.Unavailable;
        }

        if (!HasResult)
        {
            Display = AnalysisDisplay.Empty(Labels);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _cache.Clear();
    }

    partial void OnActiveTabChanged(AnalysisTab value)
    {
        OnPropertyChanged(nameof(IsHistogramTab));
        OnPropertyChanged(nameof(IsColorTab));
        OnPropertyChanged(nameof(IsQualityTab));
        OnPropertyChanged(nameof(IsDataTab));
    }

    partial void OnChartModeChanged(HistogramChartMode value)
    {
        OnPropertyChanged(nameof(IsLumaMode));
        OnPropertyChanged(nameof(IsRgbMode));
        OnPropertyChanged(nameof(IsHueMode));
        OnPropertyChanged(nameof(IsSaturationMode));
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsEyedropperActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoSampleActive));
        OnPropertyChanged(nameof(ShowNoSampleInactive));
    }

    partial void OnHasLiveSampleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoSampleActive));
        OnPropertyChanged(nameof(ShowNoSampleInactive));
    }

    private void Apply(ImageAnalysisResult result, string note)
    {
        if (_metadata is null)
        {
            return;
        }

        ResolutionText = note;
        Display = AnalysisDisplay.Build(result, _metadata, Labels, _localization.Culture, note);
        HasResult = true;
        ErrorText = null;
    }

    private void ClearLive()
    {
        LiveSample = null;
        HasLiveSample = false;
    }

    private string FormatResolutionNote(bool isOriginal, ImageAnalysisResult result) =>
        string.Format(
            _localization.Culture,
            isOriginal ? Labels.OriginalNoteFormat : Labels.PreviewNoteFormat,
            result.Width,
            result.Height);

    private AnalysisSampleView CreateSample(int x, int y, RgbColor color, byte alpha, int? index)    {
        var hsl = ColorMath.RgbToHsl(color);
        var lab = ColorMath.RgbToLab(color);
        var culture = _localization.Culture;
        return new AnalysisSampleView
        {
            Brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
            Hex = color.ToHex(),
            PositionText = $"X {x} · Y {y}",
            RgbText = $"{color.R} {color.G} {color.B}",
            HslText = string.Format(
                culture,
                "{0:0}° {1:0}% {2:0}%",
                hsl.H,
                hsl.S * 100,
                hsl.L * 100),
            LabText = string.Format(
                culture,
                "{0:0} {1:0} {2:0}",
                lab.L,
                lab.A,
                lab.B),
            AlphaText = alpha.ToString(culture),
            IndexText = index?.ToString(culture) ?? string.Empty,
            X = x,
            Y = y
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Relocalize();

    private sealed record CachedAnalysis(ImageAnalysisResult Result, bool IsOriginal);
}
