using Avalonia;

namespace Kinovea.PortableDesktop.Presentation;

/// <summary>Double-precision mapping between image pixels and viewport logical pixels.</summary>
public readonly record struct ViewportTransform
{
    private ViewportTransform(int imageWidth, int imageHeight, double scale, Point offset)
    {
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Scale = scale;
        Offset = offset;
    }

    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public double Scale { get; }
    public Point Offset { get; }

    public static ViewportTransform Create(int imageWidth, int imageHeight, Size viewport, double zoom = 1, Vector pan = default)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");
        if (viewport.Width <= 0 || viewport.Height <= 0 || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height))
            throw new ArgumentOutOfRangeException(nameof(viewport), "Viewport dimensions must be finite and positive.");
        if (!double.IsFinite(zoom) || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        if (!double.IsFinite(pan.X) || !double.IsFinite(pan.Y))
            throw new ArgumentOutOfRangeException(nameof(pan));

        double fit = Math.Min(viewport.Width / imageWidth, viewport.Height / imageHeight);
        double scale = fit * zoom;
        var offset = new Point((viewport.Width - imageWidth * scale) / 2 + pan.X, (viewport.Height - imageHeight * scale) / 2 + pan.Y);
        return new ViewportTransform(imageWidth, imageHeight, scale, offset);
    }

    public Point ImageToViewport(Point imagePoint) => new(Offset.X + imagePoint.X * Scale, Offset.Y + imagePoint.Y * Scale);

    public Point ViewportToImage(Point viewportPoint) => new((viewportPoint.X - Offset.X) / Scale, (viewportPoint.Y - Offset.Y) / Scale);

    public Rect ImageRectangle => new(Offset.X, Offset.Y, ImageWidth * Scale, ImageHeight * Scale);

    public static Vector PanForZoomAt(Point viewportPoint, Point imagePoint, Size viewport, int imageWidth, int imageHeight, double newZoom)
    {
        double fit = Math.Min(viewport.Width / imageWidth, viewport.Height / imageHeight);
        double scale = fit * newZoom;
        return new Vector(viewportPoint.X - (viewport.Width - imageWidth * scale) / 2 - imagePoint.X * scale,
            viewportPoint.Y - (viewport.Height - imageHeight * scale) / 2 - imagePoint.Y * scale);
    }
}
