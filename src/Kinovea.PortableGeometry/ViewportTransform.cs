namespace Kinovea.PortableGeometry;

/// <summary>Maps continuous image-pixel coordinates to viewport logical-pixel coordinates.</summary>
/// <remarks>
/// Both spaces use a top-left origin, with X increasing rightward and Y downward. Image
/// coordinates describe pixel edges; the right and bottom edges are at Width and Height.
/// Scale is uniform, and pan is measured in viewport logical pixels.
/// </remarks>
public readonly record struct ViewportTransform
{
    private ViewportTransform(int imageWidth, int imageHeight, double scale, Point2D offset)
    {
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Scale = scale;
        Offset = offset;
    }

    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public double Scale { get; }
    public Point2D Offset { get; }

    public static ViewportTransform Create(int imageWidth, int imageHeight, Size2D viewport, double zoom = 1, Vector2D pan = default)
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
        var offset = new Point2D((viewport.Width - imageWidth * scale) / 2 + pan.X,
            (viewport.Height - imageHeight * scale) / 2 + pan.Y);
        return new ViewportTransform(imageWidth, imageHeight, scale, offset);
    }

    public Point2D ImageToViewport(Point2D imagePoint) => new(Offset.X + imagePoint.X * Scale, Offset.Y + imagePoint.Y * Scale);

    public Point2D ViewportToImage(Point2D viewportPoint) => new((viewportPoint.X - Offset.X) / Scale, (viewportPoint.Y - Offset.Y) / Scale);

    public Rect2D ImageRectangle => new(Offset.X, Offset.Y, ImageWidth * Scale, ImageHeight * Scale);

    public static Vector2D PanForZoomAt(Point2D viewportPoint, Point2D imagePoint, Size2D viewport, int imageWidth, int imageHeight, double newZoom)
    {
        double fit = Math.Min(viewport.Width / imageWidth, viewport.Height / imageHeight);
        double scale = fit * newZoom;
        return new Vector2D(viewportPoint.X - (viewport.Width - imageWidth * scale) / 2 - imagePoint.X * scale,
            viewportPoint.Y - (viewport.Height - imageHeight * scale) / 2 - imagePoint.Y * scale);
    }
}
