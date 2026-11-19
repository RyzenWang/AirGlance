using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AirGlance.Core.Imaging;

namespace AirGlance.App.Controls;

/// <summary>
/// Interactive crop frame drawn over <see cref="ImageViewer"/>. The selection is
/// stored in normalized image coordinates, so it survives zooming and maps directly
/// to the full-resolution export.
/// </summary>
internal sealed class CropOverlay : UserControl
{
    private const double HandleRadius = 7;
    private const double MinCropPixels = 32;
    private const double DimOpacity = 0.52;

    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(138, 12, 10, 8));
    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.Parse("#D65A43"));
    private static readonly IBrush WhiteBrush = Brushes.White;
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.Parse("#F8F6F2")), 1.5);
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), 1);

    private readonly ImageViewer _viewer;
    private readonly Border _surface;
    private readonly DrawLayer _drawLayer;
    private CropRegion _crop = CropRegion.Full;
    private CropRegion? _dragStart;
    private Point _pressPoint;
    private DragMode _dragMode;
    private bool _active;
    private double? _aspectRatio;

    public CropOverlay(ImageViewer viewer)
    {
        _viewer = viewer;
        IsHitTestVisible = false;
        Focusable = true;

        _drawLayer = new DrawLayer(this) { IsHitTestVisible = false };
        _surface = new Border { Background = Brushes.Transparent };
        Content = new Grid { Children = { _drawLayer, _surface } };
    }

    public event EventHandler? CropChanged;

    public CropRegion Crop => _crop;

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            IsHitTestVisible = value;
            _surface.IsHitTestVisible = value;
            _drawLayer.InvalidateVisual();
        }
    }

    public double? AspectRatio
    {
        get => _aspectRatio;
        set
        {
            _aspectRatio = value;
            if (value is double ratio)
            {
                ApplyAspect(ratio);
            }
        }
    }

    public void Reset()
    {
        _crop = CropRegion.Full;
        CropChanged?.Invoke(this, EventArgs.Empty);
        _drawLayer.InvalidateVisual();
    }

    public void SetCrop(CropRegion crop)
    {
        var clamped = crop.Clamp();
        if (clamped == _crop)
        {
            return;
        }

        _crop = clamped;
        CropChanged?.Invoke(this, EventArgs.Empty);
        _drawLayer.InvalidateVisual();
    }

    public void ApplyAspect(double? ratio)
    {
        _aspectRatio = ratio;
        if (ratio is not double value)
        {
            InvalidateVisual();
            return;
        }

        var bounds = _viewer.GetImageBoundsOnCanvas();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Desired crop width/height in normalized units for the requested pixel ratio.
        var normalizedRatio = value * bounds.Height / bounds.Width;
        var width = _crop.Width;
        var height = width / normalizedRatio;
        if (height > 1)
        {
            height = 1;
            width = height * normalizedRatio;
        }

        if (width > 1)
        {
            width = 1;
            height = width / normalizedRatio;
        }

        _crop = new CropRegion(
            Math.Clamp(_crop.X + (_crop.Width - width) / 2, 0, 1 - width),
            Math.Clamp(_crop.Y + (_crop.Height - height) / 2, 0, 1 - height),
            width,
            height);
        CropChanged?.Invoke(this, EventArgs.Empty);
        _drawLayer.InvalidateVisual();
    }

    private void Draw(DrawingContext context)
    {
        if (!_active)
        {
            return;
        }

        var image = _viewer.GetImageBoundsOnCanvas();
        if (image.Width < 2 || image.Height < 2)
        {
            return;
        }

        var crop = ToScreenRect(image);
        if (crop.Width < 1 || crop.Height < 1)
        {
            return;
        }

        // Dim everything outside the crop but keep the photo itself as the bounds.
        FillOutside(context, image, crop);
        context.DrawRectangle(null, BorderPen, crop);

        var thirdWidth = crop.Width / 3;
        var thirdHeight = crop.Height / 3;
        for (var index = 1; index <= 2; index++)
        {
            context.DrawLine(
                GridPen,
                new Point(crop.Left + thirdWidth * index, crop.Top),
                new Point(crop.Left + thirdWidth * index, crop.Bottom));
            context.DrawLine(
                GridPen,
                new Point(crop.Left, crop.Top + thirdHeight * index),
                new Point(crop.Right, crop.Top + thirdHeight * index));
        }

        foreach (var point in HandlePoints(crop))
        {
            context.FillRectangle(HandleBrush, new Rect(
                point.X - HandleRadius / 2,
                point.Y - HandleRadius / 2,
                HandleRadius,
                HandleRadius));
            context.DrawRectangle(null, new Pen(WhiteBrush, 1), new Rect(
                point.X - HandleRadius / 2,
                point.Y - HandleRadius / 2,
                HandleRadius,
                HandleRadius));
        }
    }

    public bool BeginDrag(Point point)
    {
        if (!_active)
        {
            return false;
        }

        var image = _viewer.GetImageBoundsOnCanvas();
        var crop = ToScreenRect(image);
        var mode = HitTest(crop, point);
        if (mode == DragMode.None)
        {
            return false;
        }

        _dragMode = mode;
        _dragStart = _crop;
        _pressPoint = point;
        return true;
    }

    public void DragTo(Point current)
    {
        if (_dragMode == DragMode.None || _dragStart is not CropRegion start)
        {
            return;
        }

        var image = _viewer.GetImageBoundsOnCanvas();
        if (image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        var dx = (current.X - _pressPoint.X) / image.Width;
        var dy = (current.Y - _pressPoint.Y) / image.Height;
        _crop = Resize(start, dx, dy, image);
        CropChanged?.Invoke(this, EventArgs.Empty);
        _drawLayer.InvalidateVisual();
    }

    public void EndDrag()
    {
        _dragMode = DragMode.None;
        _dragStart = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_active)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (BeginDrag(point.Position))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        DragTo(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }

        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private CropRegion Resize(CropRegion start, double dx, double dy, Rect image)
    {
        var minX = MinCropPixels / Math.Max(1, image.Width);
        var minY = MinCropPixels / Math.Max(1, image.Height);
        double left = start.X;
        double top = start.Y;
        double right = start.X + start.Width;
        double bottom = start.Y + start.Height;

        switch (_dragMode)
        {
            case DragMode.Move:
                left = Math.Clamp(start.X + dx, 0, 1 - start.Width);
                top = Math.Clamp(start.Y + dy, 0, 1 - start.Height);
                return new CropRegion(left, top, start.Width, start.Height);
            case DragMode.West:
            case DragMode.NorthWest:
            case DragMode.SouthWest:
                left = Math.Clamp(start.X + dx, 0, right - minX);
                break;
            case DragMode.East:
            case DragMode.NorthEast:
            case DragMode.SouthEast:
                right = Math.Clamp(start.X + start.Width + dx, left + minX, 1);
                break;
        }

        switch (_dragMode)
        {
            case DragMode.North:
            case DragMode.NorthWest:
            case DragMode.NorthEast:
                top = Math.Clamp(start.Y + dy, 0, bottom - minY);
                break;
            case DragMode.South:
            case DragMode.SouthWest:
            case DragMode.SouthEast:
                bottom = Math.Clamp(start.Y + start.Height + dy, top + minY, 1);
                break;
        }

        if (_aspectRatio is double ratio && _dragMode != DragMode.Move)
        {
            var targetWidth = right - left;
            var targetHeight = targetWidth * image.Width / (ratio * image.Height);
            if (targetHeight < minY || top + targetHeight > 1)
            {
                targetHeight = Math.Max(minY, Math.Min(bottom - top, targetHeight));
                targetWidth = targetHeight * ratio * image.Height / image.Width;
            }

            if (_dragMode is DragMode.West or DragMode.NorthWest or DragMode.SouthWest)
            {
                left = Math.Max(0, right - targetWidth);
            }
            else
            {
                right = Math.Min(1, left + targetWidth);
            }

            if (_dragMode is DragMode.North or DragMode.NorthWest or DragMode.NorthEast)
            {
                top = Math.Max(0, bottom - targetHeight);
            }
            else
            {
                bottom = Math.Min(1, top + targetHeight);
            }
        }

        return new CropRegion(
            Math.Clamp(left, 0, 1),
            Math.Clamp(top, 0, 1),
            Math.Max(minX, right - left),
            Math.Max(minY, bottom - top)).Clamp();
    }

    private static void FillOutside(DrawingContext context, Rect image, Rect crop)
    {
        var top = new Rect(image.Left, image.Top, image.Width, Math.Max(0, crop.Top - image.Top));
        var bottom = new Rect(image.Left, crop.Bottom, image.Width, Math.Max(0, image.Bottom - crop.Bottom));
        var left = new Rect(image.Left, crop.Top, Math.Max(0, crop.Left - image.Left), crop.Height);
        var right = new Rect(crop.Right, crop.Top, Math.Max(0, image.Right - crop.Right), crop.Height);
        context.FillRectangle(DimBrush, top);
        context.FillRectangle(DimBrush, bottom);
        context.FillRectangle(DimBrush, left);
        context.FillRectangle(DimBrush, right);
    }

    private Rect ToScreenRect(Rect image) => new(
        image.X + _crop.X * image.Width,
        image.Y + _crop.Y * image.Height,
        _crop.Width * image.Width,
        _crop.Height * image.Height);

    private static IEnumerable<Point> HandlePoints(Rect crop)
    {
        yield return new Point(crop.Left, crop.Top);
        yield return new Point(crop.Center.X, crop.Top);
        yield return new Point(crop.Right, crop.Top);
        yield return new Point(crop.Left, crop.Center.Y);
        yield return new Point(crop.Right, crop.Center.Y);
        yield return new Point(crop.Left, crop.Bottom);
        yield return new Point(crop.Center.X, crop.Bottom);
        yield return new Point(crop.Right, crop.Bottom);
    }

    private static DragMode HitTest(Rect crop, Point point)
    {
        var near = HandleRadius;
        var left = Math.Abs(point.X - crop.Left) <= near;
        var right = Math.Abs(point.X - crop.Right) <= near;
        var top = Math.Abs(point.Y - crop.Top) <= near;
        var bottom = Math.Abs(point.Y - crop.Bottom) <= near;
        var insideX = point.X >= crop.Left - near && point.X <= crop.Right + near;
        var insideY = point.Y >= crop.Top - near && point.Y <= crop.Bottom + near;

        if (!insideX || !insideY)
        {
            return DragMode.None;
        }

        if (left && top) return DragMode.NorthWest;
        if (right && top) return DragMode.NorthEast;
        if (left && bottom) return DragMode.SouthWest;
        if (right && bottom) return DragMode.SouthEast;
        if (top) return DragMode.North;
        if (bottom) return DragMode.South;
        if (left) return DragMode.West;
        if (right) return DragMode.East;
        return crop.Contains(point) ? DragMode.Move : DragMode.None;
    }

    private sealed class DrawLayer : Control
    {
        private readonly CropOverlay _owner;

        public DrawLayer(CropOverlay owner) => _owner = owner;

        public override void Render(DrawingContext context) => _owner.Draw(context);
    }

    private enum DragMode
    {
        None,
        Move,
        North,
        South,
        East,
        West,
        NorthWest,
        NorthEast,
        SouthWest,
        SouthEast
    }
}
