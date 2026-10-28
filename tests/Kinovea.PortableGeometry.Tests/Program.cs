using Kinovea.PortableGeometry;

var assertions = 0;
var portrait = ViewportTransform.Create(276, 398, new Size2D(1000, 600));
Near(600d / 398, portrait.Scale, 1e-12, "fit preserves aspect ratio");
Near((1000 - 276 * portrait.Scale) / 2, portrait.Offset.X, 1e-12, "portrait horizontal letterbox");
Near(0, portrait.Offset.Y, 1e-12, "portrait fits viewport height");
Near(276 * portrait.Scale, portrait.ImageRectangle.Width, 1e-12, "image edge width in viewport units");
Near(600, portrait.ImageRectangle.Height, 1e-12, "image edge height in viewport units");

Point2D source = new(138.25, 199.75);
Point2D screen = portrait.ImageToViewport(source);
Point2D restored = portrait.ViewportToImage(screen);
Near(source.X, restored.X, 1e-12, "image to viewport to image X");
Near(source.Y, restored.Y, 1e-12, "image to viewport to image Y");
Point2D upperLeft = portrait.ImageToViewport(new Point2D(0, 0));
Near(portrait.Offset.X, upperLeft.X, 1e-12, "top-left edge maps to image offset");
Near(portrait.Offset.Y, upperLeft.Y, 1e-12, "top-left edge maps to image offset");

var landscape = ViewportTransform.Create(1280, 720, new Size2D(900, 700));
Near(900, landscape.ImageRectangle.Width, 1e-12, "landscape fits viewport width");
Near(506.25, landscape.ImageRectangle.Height, 1e-12, "landscape letterbox height");
var panned = ViewportTransform.Create(1280, 720, new Size2D(900, 700), 1, new Vector2D(13.5, -22));
Near(landscape.Offset.X + 13.5, panned.Offset.X, 1e-12, "pan uses viewport logical pixels on X");
Near(landscape.Offset.Y - 22, panned.Offset.Y, 1e-12, "pan uses viewport logical pixels on Y");

Point2D anchor = new(311.5, 217.25);
Point2D imageAnchor = landscape.ViewportToImage(anchor);
Vector2D zoomPan = ViewportTransform.PanForZoomAt(anchor, imageAnchor, new Size2D(900, 700), 1280, 720, 2.25);
var zoomed = ViewportTransform.Create(1280, 720, new Size2D(900, 700), 2.25, zoomPan);
Point2D unchangedAnchor = zoomed.ImageToViewport(imageAnchor);
Near(anchor.X, unchangedAnchor.X, 1e-10, "anchored zoom preserves viewport X");
Near(anchor.Y, unchangedAnchor.Y, 1e-10, "anchored zoom preserves viewport Y");

Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(0, 10, new Size2D(20, 20)), "zero image width rejected");
Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, -1, new Size2D(20, 20)), "negative image height rejected");
Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size2D(0, 20)), "zero viewport width rejected");
Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size2D(20, double.NaN)), "non-finite viewport extent rejected");
Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size2D(20, 20), 0), "zero zoom rejected");
Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size2D(20, 20), double.PositiveInfinity), "non-finite zoom rejected");
Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size2D(20, 20), 1, new Vector2D(double.NaN, 0)), "non-finite pan rejected");

Console.WriteLine($"PASS: {assertions} portable geometry assertions");

void Near(double expected, double actual, double tolerance, string name)
{
    assertions++;
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{name}: expected {expected:R}; got {actual:R}; tolerance {tolerance:R}.");
}

void Throws<TException>(Action action, string name) where TException : Exception
{
    assertions++;
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
}
