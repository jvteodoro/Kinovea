using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Kinovea.PortableDesktop.Presentation;

namespace Kinovea.PortableDesktop;

internal sealed class ImageViewport : Control
{
    private Point? dragOrigin;
    private Vector dragPan;

    public WriteableBitmap? Bitmap { get; set; }
    public double Zoom { get; private set; } = 1;
    public Vector Pan { get; private set; }
    public int WheelEventCount { get; private set; }
    public int PointerPressCount { get; private set; }
    public int PointerMoveCount { get; private set; }
    public Point LastWheelPosition { get; private set; }
    public event Action? ImageRendered;
    public event Action<double>? ZoomChanged;
    public event Action? InteractionObserved;

    public ViewportTransform? Transform => Bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0
        ? null
        : ViewportTransform.Create(Bitmap.PixelSize.Width, Bitmap.PixelSize.Height, Bounds.Size, Zoom, Pan);

    public void SetBitmap(WriteableBitmap? bitmap)
    {
        Bitmap = bitmap;
        Zoom = 1;
        Pan = default;
        ZoomChanged?.Invoke(Zoom);
        InvalidateVisual();
    }

    public void Fit()
    {
        Zoom = 1;
        Pan = default;
        ZoomChanged?.Invoke(Zoom);
        InvalidateVisual();
    }

    public void ChangeZoom(double zoom)
    {
        SetZoomAt(zoom, new Point(Bounds.Width / 2, Bounds.Height / 2));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#11151b")), new Rect(Bounds.Size));
        if (Bitmap is null || Transform is not { } transform)
            return;

        context.DrawImage(Bitmap, new Rect(0, 0, Bitmap.PixelSize.Width, Bitmap.PixelSize.Height), transform.ImageRectangle);
        ImageRendered?.Invoke();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        WheelEventCount++;
        LastWheelPosition = e.GetPosition(this);
        InteractionObserved?.Invoke();
        if (Bitmap is null)
            return;
        double multiplier = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
        SetZoomAt(Zoom * multiplier, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerPressCount++;
        InteractionObserved?.Invoke();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            dragOrigin = e.GetPosition(this);
            dragPan = Pan;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        PointerMoveCount++;
        if (dragOrigin is { } origin && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Point current = e.GetPosition(this);
            Pan = dragPan + (current - origin);
            InvalidateVisual();
            InteractionObserved?.Invoke();
            e.Handled = true;
        }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragOrigin is not null)
        {
            dragOrigin = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    private void SetZoomAt(double zoom, Point anchor)
    {
        if (Bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        double next = Math.Clamp(zoom, 0.1, 16);
        Point imagePoint = ViewportTransform.Create(Bitmap.PixelSize.Width, Bitmap.PixelSize.Height, Bounds.Size, Zoom, Pan).ViewportToImage(anchor);
        Pan = ViewportTransform.PanForZoomAt(anchor, imagePoint, Bounds.Size, Bitmap.PixelSize.Width, Bitmap.PixelSize.Height, next);
        Zoom = next;
        ZoomChanged?.Invoke(Zoom);
        InvalidateVisual();
    }
}
