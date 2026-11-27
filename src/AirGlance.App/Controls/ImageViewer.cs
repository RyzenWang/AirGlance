using System.Diagnostics;
using System.Globalization;
using AirGlance.App.Imaging;
using AirGlance.App.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AirGlance.App.Controls;

public enum ViewerZoomMode
{
    FitWindow,
    FitWidth,
    FitHeight,
    Actual,
    Manual
}

public sealed class ZoomChangedEventArgs(double percent) : EventArgs
{
    public double Percent { get; } = percent;
}

public sealed class ViewportChangedEventArgs(int pixelWidth, int pixelHeight) : EventArgs
{
    public int PixelWidth { get; } = pixelWidth;

    public int PixelHeight { get; } = pixelHeight;
}

public sealed class ViewerPickEventArgs(Point position) : EventArgs
{
    public Point Position { get; } = position;
}

public readonly record struct ViewerSample(int X, int Y, Color Color, int Index);

/// <summary>
/// The image canvas. It owns zoom and pan so wheel anchoring stays correct, draws the
/// checkerboard and the nearest/high-quality choice, and reports the visible zoom so the
/// status bar and the decoder can react.
/// </summary>
public sealed class ImageViewer : Control
{
    private const double MinimumZoom = 0.01;
    private const double MaximumZoom = 32.0;
    private const double RegionTileZoomThreshold = 0.30;
    private const double PanMargin = 48;

    public static readonly StyledProperty<WriteableBitmap?> SourceProperty =
        AvaloniaProperty.Register<ImageViewer, WriteableBitmap?>(nameof(Source));

    public static readonly StyledProperty<int> SourcePixelWidthProperty =
        AvaloniaProperty.Register<ImageViewer, int>(nameof(SourcePixelWidth));

    public static readonly StyledProperty<int> SourcePixelHeightProperty =
        AvaloniaProperty.Register<ImageViewer, int>(nameof(SourcePixelHeight));

    public static readonly StyledProperty<double> ImageScaleProperty =
        AvaloniaProperty.Register<ImageViewer, double>(nameof(ImageScale), 1.0);

    public static readonly StyledProperty<ViewerZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<ImageViewer, ViewerZoomMode>(nameof(ZoomMode), ViewerZoomMode.FitWindow);

    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<ImageViewer, double>(nameof(ZoomFactor), 1.0);

    public static readonly StyledProperty<bool> CheckerboardEnabledProperty =
        AvaloniaProperty.Register<ImageViewer, bool>(nameof(CheckerboardEnabled), true);

    public static readonly StyledProperty<IBrush?> CheckerLightBrushProperty =
        AvaloniaProperty.Register<ImageViewer, IBrush?>(nameof(CheckerLightBrush));

    public static readonly StyledProperty<IBrush?> CheckerDarkBrushProperty =
        AvaloniaProperty.Register<ImageViewer, IBrush?>(nameof(CheckerDarkBrush));

    private readonly DispatcherTimer _fadeTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _zoomFallbackTimer;
    private readonly Stopwatch _zoomClock = new();
    private DrawingBrush? _checkerBrush;
    private IBrush? _checkerBrushLight;
    private IBrush? _checkerBrushDark;
    private double _panX;
    private double _panY;
    private bool _isDragging;
    private Point _dragStart;
    private double _dragPanX;
    private double _dragPanY;
    private DateTime? _fadeStarted;
    private bool _firstFrameRaised;
    private bool _suppressZoomReset;
    private bool _cursorHidden;
    private bool _hasSource;
    private Size _lastViewport;
    private TiledImageSource? _tiles;
    private AnimatedImage? _animation;
    private int _animationIndex;
    private bool _zoomAnimating;
    private double _zoomAnimFrom;
    private double _zoomAnimTo;
    private double _zoomAnimAnchorX;
    private double _zoomAnimAnchorY;
    private double _zoomAnimAnchorU;
    private double _zoomAnimAnchorV;
    private double _zoomAnimDurationMs;
    private ViewerZoomMode _zoomAnimFinalMode;

    /// <summary>
    /// Shared cursors. A new <see cref="Cursor"/> per pointer move churned a platform
    /// handle for every event; the platform object behind one is reusable, so a single
    /// instance per shape is enough and lives for the process. They are deliberately
    /// never disposed: one viewer disposing them would break every other viewer.
    /// </summary>
    private static class Cursors
    {
        public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
        public static readonly Cursor Hand = new(StandardCursorType.Hand);
        public static readonly Cursor SizeAll = new(StandardCursorType.SizeAll);
        public static readonly Cursor Cross = new(StandardCursorType.Cross);
        public static readonly Cursor None = new(StandardCursorType.None);
    }

    public ImageViewer()
    {
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.Arrow;

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += OnFadeTick;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _animationTimer.Tick += OnAnimationTick;
        _zoomFallbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        _zoomFallbackTimer.Tick += OnZoomFallbackTick;

        AddHandler(Gestures.PinchEvent, OnPinch);
        AddHandler(Gestures.PinchEndedEvent, OnPinchEnded);

        SizeChanged += (_, _) => OnViewportChanged();
    }

    public event EventHandler<ZoomChangedEventArgs>? ZoomChanged;

    public event EventHandler<ViewportChangedEventArgs>? ViewportChanged;

    /// <summary>Raised once, after the first image has actually been painted.</summary>
    public event EventHandler? FirstFramePresented;

    public event EventHandler<ViewerPickEventArgs>? PickHover;

    public event EventHandler<ViewerPickEventArgs>? PickRequested;

    private bool _isPicking;

    private IReadOnlyList<ViewerSample>? _samples;

    /// <summary>When true, the pointer reads pixels instead of panning.</summary>
    public bool IsPicking
    {
        get => _isPicking;
        set
        {
            if (_isPicking == value)
            {
                return;
            }

            _isPicking = value;
            UpdateCursor();
        }
    }

    /// <summary>Pinned samples drawn over the image, in image-pixel coordinates.</summary>
    public IReadOnlyList<ViewerSample>? Samples
    {
        get => _samples;
        set
        {
            _samples = value;
            InvalidateVisual();
        }
    }

    public WriteableBitmap? Source
    {
        get => GetValue(SourceProperty);
        private set => SetValue(SourceProperty, value);
    }

    public int SourcePixelWidth
    {
        get => GetValue(SourcePixelWidthProperty);
        private set => SetValue(SourcePixelWidthProperty, value);
    }

    public int SourcePixelHeight
    {
        get => GetValue(SourcePixelHeightProperty);
        private set => SetValue(SourcePixelHeightProperty, value);
    }

    public double ImageScale
    {
        get => GetValue(ImageScaleProperty);
        private set => SetValue(ImageScaleProperty, value);
    }

    public ViewerZoomMode ZoomMode
    {
        get => GetValue(ZoomModeProperty);
        private set => SetValue(ZoomModeProperty, value);
    }

    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        private set => SetValue(ZoomFactorProperty, value);
    }

    public bool CheckerboardEnabled
    {
        get => GetValue(CheckerboardEnabledProperty);
        set => SetValue(CheckerboardEnabledProperty, value);
    }

    public IBrush? CheckerLightBrush
    {
        get => GetValue(CheckerLightBrushProperty);
        set => SetValue(CheckerLightBrushProperty, value);
    }

    public IBrush? CheckerDarkBrush
    {
        get => GetValue(CheckerDarkBrushProperty);
        set => SetValue(CheckerDarkBrushProperty, value);
    }

    public bool CanPan
    {
        get
        {
            var layout = CalculateLayout();
            return layout.IsValid &&
                   (layout.ImageWidth > Bounds.Width + 0.5 || layout.ImageHeight > Bounds.Height + 0.5);
        }
    }

    public bool CanPanHorizontally
    {
        get
        {
            var layout = CalculateLayout();
            return layout.IsValid && layout.ImageWidth > Bounds.Width + 0.5;
        }
    }

    public bool HasImage => Source is not null || _tiles is not null;

    public double ZoomPercent => CalculateLayout().Zoom * 100;

    /// <summary>
    /// The photo's rectangle in viewer coordinates. Used by the crop overlay so its
    /// handles stay on the image while zooming and panning.
    /// </summary>
    public Rect GetImageBoundsOnCanvas()
    {
        var layout = CalculateLayout();
        return layout.IsValid
            ? new Rect(layout.Left, layout.Top, layout.ImageWidth, layout.ImageHeight)
            : default;
    }

    /// <summary>
    /// Maps a viewer point to a pixel in the displayed bitmap. Returns false outside the
    /// image or when the canvas is showing tiles without a display bitmap.
    /// </summary>
    public bool TryGetImagePixel(Point point, out int x, out int y)
    {
        x = 0;
        y = 0;
        var source = Source;
        if (source is null || !HasImage)
        {
            return false;
        }

        var layout = CalculateLayout();
        if (!layout.IsValid)
        {
            return false;
        }

        var rect = new Rect(layout.Left, layout.Top, layout.ImageWidth, layout.ImageHeight);
        if (!rect.Contains(point))
        {
            return false;
        }

        var size = source.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return false;
        }

        x = Math.Clamp(
            (int)((point.X - layout.Left) / layout.ImageWidth * size.Width),
            0,
            size.Width - 1);
        y = Math.Clamp(
            (int)((point.Y - layout.Top) / layout.ImageHeight * size.Height),
            0,
            size.Height - 1);
        return true;
    }

    public void SetTiles(TiledImageSource? tiles)
    {
        StopAnimation();
        if (_tiles is not null)
        {
            _tiles.TileAvailable -= OnTileAvailable;
        }

        _tiles = tiles;
        if (tiles is not null)
        {
            tiles.TileAvailable += OnTileAvailable;
            SourcePixelWidth = tiles.PixelWidth;
            SourcePixelHeight = tiles.PixelHeight;
        }

        _hasSource = HasImage;
        UpdateInterpolation();
        InvalidateVisual();
    }

    private void OnTileAvailable(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);

    public void SetImage(
        WriteableBitmap? bitmap,
        int sourcePixelWidth,
        int sourcePixelHeight,
        double imageScale,
        bool resetView = true,
        bool animateImage = true)
    {
        StopAnimation();
        var animate = animateImage && _hasSource && bitmap is not null && ReferenceEquals(bitmap, Source) == false;

        if (_tiles is not null)
        {
            _tiles.TileAvailable -= OnTileAvailable;
        }

        _tiles = null;
        Source = bitmap;
        SourcePixelWidth = sourcePixelWidth;
        SourcePixelHeight = sourcePixelHeight;
        ImageScale = imageScale <= 0 ? 1.0 : imageScale;
        _hasSource = bitmap is not null;

        if (resetView)
        {
            _panX = 0;
            _panY = 0;
        }

        if (animate)
        {
            _fadeStarted = DateTime.UtcNow;
            _fadeTimer.Start();
        }
        else
        {
            _fadeStarted = null;
            _fadeTimer.Stop();
        }

        UpdateInterpolation();
        InvalidateVisual();
        RaiseZoomChanged();
    }

    public void SetAnimation(AnimatedImage animation, bool resetView = true)
    {
        ArgumentNullException.ThrowIfNull(animation);
        StopAnimation();
        StopZoomAnimation();

        if (_tiles is not null)
        {
            _tiles.TileAvailable -= OnTileAvailable;
        }

        _tiles = null;
        _animation = animation;
        _animationIndex = 0;
        Source = animation.Frames[0];
        SourcePixelWidth = animation.SourceWidth;
        SourcePixelHeight = animation.SourceHeight;
        ImageScale = animation.Scale <= 0 ? 1.0 : animation.Scale;
        _hasSource = true;

        if (resetView)
        {
            _panX = 0;
            _panY = 0;
        }

        _fadeStarted = null;
        _fadeTimer.Stop();
        UpdateInterpolation();
        InvalidateVisual();
        RaiseZoomChanged();
        StartAnimationTimer();
    }

    private void StartAnimationTimer()
    {
        if (_animation is null || _animation.FrameCount <= 1)
        {
            _animationTimer.Stop();
            return;
        }

        _animationTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp(_animation.DelaysMilliseconds[_animationIndex], 20, 10_000));
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _animation = null;
        _animationIndex = 0;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_animation is null || _animation.FrameCount <= 1)
        {
            _animationTimer.Stop();
            return;
        }

        _animationIndex = (_animationIndex + 1) % _animation.FrameCount;
        Source = _animation.Frames[_animationIndex];
        _animationTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp(_animation.DelaysMilliseconds[_animationIndex], 20, 10_000));
        InvalidateVisual();
    }

    public void ClearImage()
    {
        StopAnimation();
        Source = null;
        SourcePixelWidth = 0;
        SourcePixelHeight = 0;
        if (_tiles is not null)
        {
            _tiles.TileAvailable -= OnTileAvailable;
        }

        _hasSource = false;
        _tiles = null;
        _panX = 0;
        _panY = 0;
        _fadeTimer.Stop();
        _fadeStarted = null;
        InvalidateVisual();
        RaiseZoomChanged();
    }

    public void SetZoomMode(ViewerZoomMode mode, double? factor = null)
    {
        StopZoomAnimation();
        if (_suppressZoomReset)
        {
            return;
        }

        ZoomMode = mode;
        if (mode == ViewerZoomMode.Manual && factor is not null)
        {
            ZoomFactor = Math.Clamp(factor.Value, MinimumZoom, MaximumZoom);
        }

        _panX = 0;
        _panY = 0;
        AfterZoomChange();
    }

    public void ZoomIn() => ZoomAtCenter(1.25);

    public void ZoomOut() => ZoomAtCenter(1 / 1.25);

    public void ZoomAtCenter(double factor)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        ZoomAt(center, factor);
    }

    public void AnimateZoomMode(ViewerZoomMode mode, Point? anchor = null, double durationMs = 260)
    {
        if (!HasImage || mode == ViewerZoomMode.Manual)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var targetAnchor = mode is ViewerZoomMode.FitWindow or ViewerZoomMode.FitWidth
            or ViewerZoomMode.FitHeight
            ? center
            : anchor ?? center;
        StartZoomAnimation(ZoomForMode(mode, null), targetAnchor, durationMs, mode);
    }

    public void CycleZoomPreset()
    {
        if (ZoomMode is ViewerZoomMode.Actual or (ViewerZoomMode.Manual))
        {
            AnimateZoomMode(ViewerZoomMode.FitWindow);
        }
        else
        {
            AnimateZoomMode(ViewerZoomMode.Actual);
        }
    }

    public void PanBy(double x, double y)
    {
        if (!HasImage)
        {
            return;
        }

        _panX += x;
        _panY += y;
        ClampPanIntoView();
        InvalidateVisual();
    }

    public void ApplySavedZoom(string? mode, double factor)
    {
        switch (mode)
        {
            case "fit-width":
                SetZoomMode(ViewerZoomMode.FitWidth);
                break;
            case "fit-height":
                SetZoomMode(ViewerZoomMode.FitHeight);
                break;
            case "actual":
                SetZoomMode(ViewerZoomMode.Actual);
                break;
            case "manual":
                SetZoomMode(ViewerZoomMode.Manual, factor);
                break;
            default:
                SetZoomMode(ViewerZoomMode.FitWindow);
                break;
        }
    }

    public void HideCursor()
    {
        _cursorHidden = true;
        Cursor = Cursors.None;
    }

    public void ShowCursor()
    {
        _cursorHidden = false;
        UpdateCursor();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var source = Source;
        if (!HasImage)
        {
            return;
        }

        var layout = CalculateLayout();
        if (!layout.IsValid)
        {
            return;
        }

        var imageRect = new Rect(layout.Left, layout.Top, layout.ImageWidth, layout.ImageHeight);

        if (CheckerboardEnabled)
        {
            var brush = GetCheckerBrush();
            if (brush is not null)
            {
                using (context.PushClip(imageRect.Intersect(new Rect(Bounds.Size))))
                {
                    context.FillRectangle(brush, imageRect);
                }
            }
        }

        var opacity = 1.0;
        if (_fadeStarted is { } fadeStarted)
        {
            var elapsed = (DateTime.UtcNow - fadeStarted).TotalMilliseconds;
            opacity = Math.Clamp(elapsed / 80.0, 0, 1);
        }

        using (context.PushOpacity(opacity))
        {
            var useRegionTiles = _tiles is not null &&
                                 layout.Zoom >= RegionTileZoomThreshold;
            if (useRegionTiles)
            {
                if (source is not null)
                {
                    context.DrawImage(source, imageRect);
                }

                DrawTiles(context, layout);
            }
            else if (source is not null)
            {
                context.DrawImage(source, imageRect);
            }
        }

        DrawSamples(context, layout);

        if (!_firstFrameRaised && opacity >= 0.99)
        {
            _firstFrameRaised = true;
            // Trace the draw itself, so a startup trace brackets the render pass rather
            // than only the load. The callback below is queued: Background would put it
            // behind the panel construction that OnOpened also queues, which inflates
            // the first-frame number by work that happens after the pixels.
            StartupTrace.Mark("first-frame");
            Dispatcher.UIThread.Post(
                () => FirstFramePresented?.Invoke(this, EventArgs.Empty),
                DispatcherPriority.Render);
        }
    }

    private void DrawSamples(DrawingContext context, Layout layout)
    {
        var samples = _samples;
        var source = Source;
        if (samples is null || samples.Count == 0 || source is null)
        {
            return;
        }

        var size = source.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            foreach (var sample in samples)
            {
                var x = layout.Left + (sample.X + 0.5) / size.Width * layout.ImageWidth;
                var y = layout.Top + (sample.Y + 0.5) / size.Height * layout.ImageHeight;
                var center = new Point(x, y);
                context.DrawEllipse(
                    new SolidColorBrush(sample.Color),
                    new Pen(Brushes.White, 2),
                    center,
                    9,
                    9);
                context.DrawEllipse(
                    null,
                    new Pen(new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)), 1),
                    center,
                    10.5,
                    10.5);

                var luminance = (0.2126 * sample.Color.R +
                                 0.7152 * sample.Color.G +
                                 0.0722 * sample.Color.B) / 255.0;
                var text = new FormattedText(
                    sample.Index.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    8,
                    luminance > 0.55 ? Brushes.Black : Brushes.White);
                context.DrawText(
                    text,
                    new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
            }
        }
    }

    private void DrawTiles(DrawingContext context, Layout layout)
    {
        if (_tiles is null || SourcePixelWidth <= 0 || SourcePixelHeight <= 0)
        {
            return;
        }

        var scale = layout.ImageWidth / SourcePixelWidth;
        if (scale <= 0)
        {
            return;
        }

        var left = Math.Max(0, (0 - layout.Left) / scale);
        var top = Math.Max(0, (0 - layout.Top) / scale);
        var right = Math.Min(SourcePixelWidth, (Bounds.Width - layout.Left) / scale);
        var bottom = Math.Min(SourcePixelHeight, (Bounds.Height - layout.Top) / scale);
        if (right <= left || bottom <= top)
        {
            return;
        }

        var firstColumn = Math.Max(0, (int)Math.Floor(left / TiledImageSource.TileSize));
        var lastColumn = Math.Min(
            _tiles.TileColumns - 1,
            (int)Math.Floor((right - 0.01) / TiledImageSource.TileSize));
        var firstRow = Math.Max(0, (int)Math.Floor(top / TiledImageSource.TileSize));
        var lastRow = Math.Min(
            _tiles.TileRows - 1,
            (int)Math.Floor((bottom - 0.01) / TiledImageSource.TileSize));

        using var clip = context.PushClip(new Rect(Bounds.Size));
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var tile = _tiles.GetTile(column, row);
                if (tile is null)
                {
                    continue;
                }

                var tileWidth = Math.Min(
                    TiledImageSource.TileSize,
                    _tiles.PixelWidth - column * TiledImageSource.TileSize);
                var tileHeight = Math.Min(
                    TiledImageSource.TileSize,
                    _tiles.PixelHeight - row * TiledImageSource.TileSize);
                var destination = new Rect(
                    layout.Left + column * TiledImageSource.TileSize * scale,
                    layout.Top + row * TiledImageSource.TileSize * scale,
                    tileWidth * scale,
                    tileHeight * scale);
                context.DrawImage(tile, destination);
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!HasImage)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            PanBy(e.Delta.Y * 80 + e.Delta.X * 80, 0);
        }
        else
        {
            // The wheel zooms around the pointer. Paging stays on the arrow keys,
            // PgUp/PgDn, Space/Backspace and the edge buttons.
            var factor = Math.Pow(1.25, e.Delta.Y);
            ZoomAt(e.GetPosition(this), factor);
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!HasImage)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (IsPicking && point.Properties.IsLeftButtonPressed)
        {
            PickRequested?.Invoke(this, new ViewerPickEventArgs(e.GetPosition(this)));
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);

        if (e.ClickCount == 2)
        {
            ToggleActualAndFit(position);
            e.Handled = true;
            return;
        }

        StopZoomAnimation();
        _isDragging = true;
        _dragStart = position;
        _dragPanX = _panX;
        _dragPanY = _panY;
        e.Pointer.Capture(this);
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!HasImage)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (IsPicking)
        {
            PickHover?.Invoke(this, new ViewerPickEventArgs(position));
            UpdateCursor();
            return;
        }

        if (_isDragging)
        {
            _panX = _dragPanX + (position.X - _dragStart.X);
            _panY = _dragPanY + (position.Y - _dragStart.Y);
            ClampPanIntoView();
            InvalidateVisual();
        }
        else if (!_cursorHidden)
        {
            UpdateCursor();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            UpdateCursor();
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isDragging && !_cursorHidden)
        {
            Cursor = IsPicking ? Cursors.Cross : Cursors.Arrow;
        }
    }

    private void OnPinch(object? sender, PinchEventArgs pinch)
    {
        if (!HasImage)
        {
            return;
        }

        StopZoomAnimation();
        ZoomAt(pinch.ScaleOrigin, Math.Max(0.1, pinch.Scale), animate: false);
        pinch.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        e.Handled = true;
    }

    private void ToggleActualAndFit(Point position)
    {
        if (ZoomMode is ViewerZoomMode.Actual or ViewerZoomMode.Manual)
        {
            AnimateZoomMode(ViewerZoomMode.FitWindow, durationMs: 280);
            return;
        }

        AnimateZoomMode(ViewerZoomMode.Actual, position, 280);
    }

    private void ZoomAt(Point anchor, double factor, bool animate = true)
    {
        if (!HasImage || factor <= 0)
        {
            return;
        }

        var old = CalculateLayout();
        if (!old.IsValid)
        {
            return;
        }

        if (animate)
        {
            var baseZoom = _zoomAnimating ? _zoomAnimTo : old.Zoom;
            var target = Math.Clamp(baseZoom * factor, MinimumZoom, MaximumZoom);
            StartZoomAnimation(target, anchor, 130, ViewerZoomMode.Manual);
            return;
        }

        var newZoom = Math.Clamp(old.Zoom * factor, MinimumZoom, MaximumZoom);
        if (Math.Abs(newZoom - old.Zoom) < 0.000001)
        {
            return;
        }

        var u = Math.Clamp((anchor.X - old.Left) / old.ImageWidth, 0, 1);
        var v = Math.Clamp((anchor.Y - old.Top) / old.ImageHeight, 0, 1);

        _suppressZoomReset = true;
        ZoomMode = ViewerZoomMode.Manual;
        ZoomFactor = newZoom;
        _suppressZoomReset = false;

        var scaling = GetScaling();
        var imageWidth = SourcePixelWidth * newZoom / scaling;
        var imageHeight = SourcePixelHeight * newZoom / scaling;

        _panX = anchor.X - u * imageWidth - (Bounds.Width / 2 - imageWidth / 2);
        _panY = anchor.Y - v * imageHeight - (Bounds.Height / 2 - imageHeight / 2);
        ClampPanIntoView();
        AfterZoomChange();
    }

    private double ZoomForMode(ViewerZoomMode mode, double? factor)
    {
        if (SourcePixelWidth <= 0 || SourcePixelHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return ZoomFactor;
        }

        var scaling = GetScaling();
        var zoom = mode switch
        {
            ViewerZoomMode.FitWindow => Math.Min(
                1.0,
                Math.Min(Bounds.Width * scaling / SourcePixelWidth, Bounds.Height * scaling / SourcePixelHeight)),
            ViewerZoomMode.FitWidth => Math.Min(1.0, Bounds.Width * scaling / SourcePixelWidth),
            ViewerZoomMode.FitHeight => Math.Min(1.0, Bounds.Height * scaling / SourcePixelHeight),
            ViewerZoomMode.Actual => 1.0,
            _ => factor ?? ZoomFactor
        };
        return Math.Clamp(zoom, MinimumZoom, MaximumZoom);
    }

    private void StartZoomAnimation(
        double targetZoom,
        Point anchor,
        double durationMs,
        ViewerZoomMode finalMode)
    {
        if (!HasImage || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var current = CalculateLayout();
        if (!current.IsValid || current.ImageWidth <= 0 || current.ImageHeight <= 0)
        {
            return;
        }

        targetZoom = Math.Clamp(targetZoom, MinimumZoom, MaximumZoom);
        if (Math.Abs(targetZoom - current.Zoom) < 0.000001)
        {
            if (finalMode != ViewerZoomMode.Manual)
            {
                SetZoomMode(finalMode);
            }

            return;
        }

        var u = Math.Clamp((anchor.X - current.Left) / current.ImageWidth, 0, 1);
        var v = Math.Clamp((anchor.Y - current.Top) / current.ImageHeight, 0, 1);

        _zoomAnimFrom = current.Zoom;
        _zoomAnimTo = targetZoom;
        _zoomAnimAnchorX = anchor.X;
        _zoomAnimAnchorY = anchor.Y;
        _zoomAnimAnchorU = u;
        _zoomAnimAnchorV = v;
        _zoomAnimDurationMs = Math.Max(1, durationMs);
        _zoomAnimFinalMode = finalMode;

        _suppressZoomReset = true;
        ZoomMode = ViewerZoomMode.Manual;
        ZoomFactor = current.Zoom;
        _suppressZoomReset = false;

        var scaling = GetScaling();
        var imageWidth = SourcePixelWidth * current.Zoom / scaling;
        var imageHeight = SourcePixelHeight * current.Zoom / scaling;
        _panX = anchor.X - u * imageWidth - (Bounds.Width / 2 - imageWidth / 2);
        _panY = anchor.Y - v * imageHeight - (Bounds.Height / 2 - imageHeight / 2);
        ClampPanIntoView();

        _zoomClock.Restart();
        _zoomAnimating = true;
        _zoomFallbackTimer.Start();
    }

    private void OnZoomFallbackTick(object? sender, EventArgs e)
    {
        if (_zoomAnimating)
        {
            OnZoomAnimationFrame();
        }
    }

    private void OnZoomAnimationFrame()
    {
        if (!_zoomAnimating || !HasImage)
        {
            StopZoomAnimation();
            return;
        }

        var progress = Math.Clamp(_zoomClock.Elapsed.TotalMilliseconds / _zoomAnimDurationMs, 0, 1);
        if (progress >= 1)
        {
            FinishZoomAnimation();
            return;
        }

        var eased = 1 - Math.Pow(1 - progress, 3);
        var zoom = _zoomAnimFrom * Math.Pow(_zoomAnimTo / _zoomAnimFrom, eased);
        ZoomFactor = zoom;

        var scaling = GetScaling();
        var imageWidth = SourcePixelWidth * zoom / scaling;
        var imageHeight = SourcePixelHeight * zoom / scaling;
        _panX = _zoomAnimAnchorX - _zoomAnimAnchorU * imageWidth - (Bounds.Width / 2 - imageWidth / 2);
        _panY = _zoomAnimAnchorY - _zoomAnimAnchorV * imageHeight - (Bounds.Height / 2 - imageHeight / 2);
        ClampPanIntoView();
        UpdateInterpolation();
        InvalidateVisual();
        RaiseZoomChanged();
    }

    private void FinishZoomAnimation()
    {
        _zoomAnimating = false;
        _zoomFallbackTimer.Stop();

        if (_zoomAnimFinalMode != ViewerZoomMode.Manual)
        {
            ZoomMode = _zoomAnimFinalMode;
            if (_zoomAnimFinalMode is ViewerZoomMode.FitWindow or ViewerZoomMode.FitWidth
                or ViewerZoomMode.FitHeight)
            {
                _panX = 0;
                _panY = 0;
            }
        }
        else
        {
            ZoomFactor = _zoomAnimTo;
        }

        UpdateInterpolation();
        InvalidateVisual();
        RaiseZoomChanged();
    }

    private void StopZoomAnimation()
    {
        _zoomAnimating = false;
        _zoomFallbackTimer.Stop();
    }

    private void AfterZoomChange()
    {
        UpdateInterpolation();
        InvalidateVisual();
        RaiseZoomChanged();
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        if (_fadeStarted is null)
        {
            _fadeTimer.Stop();
            return;
        }

        if ((DateTime.UtcNow - _fadeStarted.Value).TotalMilliseconds > 110)
        {
            _fadeStarted = null;
            _fadeTimer.Stop();
        }

        InvalidateVisual();
    }

    private void OnViewportChanged()
    {
        var scaling = GetScaling();
        var viewport = new Size(Bounds.Width, Bounds.Height);
        if (Math.Abs(viewport.Width - _lastViewport.Width) < 0.5 &&
            Math.Abs(viewport.Height - _lastViewport.Height) < 0.5)
        {
            return;
        }

        _lastViewport = viewport;
        if (!HasImage)
        {
            return;
        }

        ClampPanIntoView();
        InvalidateVisual();
        RaiseZoomChanged();

        if (viewport.Width > 0 && viewport.Height > 0)
        {
            ViewportChanged?.Invoke(
                this,
                new ViewportChangedEventArgs(
                    Math.Max(1, (int)Math.Round(viewport.Width * scaling)),
                    Math.Max(1, (int)Math.Round(viewport.Height * scaling))));
        }
    }

    private void ClampPanIntoView()
    {
        var layout = CalculateLayout();
        if (!layout.IsValid)
        {
            return;
        }

        if (ZoomMode is ViewerZoomMode.FitWindow or ViewerZoomMode.FitWidth ||
            layout.ImageWidth <= Bounds.Width)
        {
            _panX = 0;
        }
        else
        {
            var limit = (layout.ImageWidth + Bounds.Width) / 2 - PanMargin;
            _panX = Math.Clamp(_panX, -limit, limit);
        }

        if (ZoomMode is ViewerZoomMode.FitWindow or ViewerZoomMode.FitHeight ||
            layout.ImageHeight <= Bounds.Height)
        {
            _panY = 0;
        }
        else
        {
            var limit = (layout.ImageHeight + Bounds.Height) / 2 - PanMargin;
            _panY = Math.Clamp(_panY, -limit, limit);
        }
    }

    private Layout CalculateLayout()
    {
        var source = Source;
        if (!HasImage || SourcePixelWidth <= 0 || SourcePixelHeight <= 0 ||
            Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return default;
        }

        var scaling = GetScaling();
        var sourceWidth = SourcePixelWidth;
        var sourceHeight = SourcePixelHeight;

        var zoom = ZoomMode switch
        {
            ViewerZoomMode.FitWindow => Math.Min(
                1.0,
                Math.Min(Bounds.Width * scaling / sourceWidth, Bounds.Height * scaling / sourceHeight)),
            ViewerZoomMode.FitWidth => Math.Min(1.0, Bounds.Width * scaling / sourceWidth),
            ViewerZoomMode.FitHeight => Math.Min(1.0, Bounds.Height * scaling / sourceHeight),
            ViewerZoomMode.Actual => 1.0,
            _ => ZoomFactor
        };

        zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);

        var imageWidth = sourceWidth * zoom / scaling;
        var imageHeight = sourceHeight * zoom / scaling;

        var panX = _panX;
        var panY = _panY;

        if (ZoomMode == ViewerZoomMode.FitWindow)
        {
            panX = 0;
            panY = 0;
        }
        else if (ZoomMode == ViewerZoomMode.FitWidth)
        {
            panX = 0;
        }

        if (imageWidth <= Bounds.Width)
        {
            panX = 0;
        }
        else
        {
            var limit = (imageWidth + Bounds.Width) / 2 - PanMargin;
            panX = Math.Clamp(panX, -limit, limit);
        }

        if (imageHeight <= Bounds.Height)
        {
            panY = 0;
        }
        else
        {
            var limit = (imageHeight + Bounds.Height) / 2 - PanMargin;
            panY = Math.Clamp(panY, -limit, limit);
        }

        return new Layout(
            zoom,
            Bounds.Width / 2 - imageWidth / 2 + panX,
            Bounds.Height / 2 - imageHeight / 2 + panY,
            imageWidth,
            imageHeight);
    }

    private void UpdateInterpolation()
    {
        var zoom = CalculateLayout().Zoom;
        RenderOptions.SetBitmapInterpolationMode(
            this,
            zoom < 1.0 ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None);
    }

    private void RaiseZoomChanged()
    {
        ZoomChanged?.Invoke(this, new ZoomChangedEventArgs(ZoomPercent));
    }

    private void UpdateCursor()
    {
        Cursor = IsPicking
            ? Cursors.Cross
            : CanPan
                ? Cursors.Hand
                : Cursors.Arrow;
    }

    private double GetScaling()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return scaling <= 0 ? 1.0 : scaling;
    }

    private DrawingBrush? GetCheckerBrush()
    {
        if (ReferenceEquals(_checkerBrushLight, CheckerLightBrush) &&
            ReferenceEquals(_checkerBrushDark, CheckerDarkBrush))
        {
            return _checkerBrush;
        }

        _checkerBrushLight = CheckerLightBrush;
        _checkerBrushDark = CheckerDarkBrush;

        if (CheckerLightBrush is null || CheckerDarkBrush is null)
        {
            _checkerBrush = null;
            return null;
        }

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Brush = CheckerLightBrush,
            Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16))
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = CheckerDarkBrush,
            Geometry = Geometry.Parse("M0,0 H8 V8 H0 Z M8,8 H16 V16 H8 Z")
        });

        _checkerBrush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            SourceRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute)
        };

        return _checkerBrush;
    }

    private readonly record struct Layout(
        double Zoom,
        double Left,
        double Top,
        double ImageWidth,
        double ImageHeight)
    {
        public bool IsValid => Zoom > 0 && ImageWidth > 0 && ImageHeight > 0;
    }
}




