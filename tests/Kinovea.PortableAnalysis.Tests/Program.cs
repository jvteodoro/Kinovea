using System.Numerics;
using System.Text.Json;
using Kinovea.PortableAnalysis;

int checks = 0;
try
{
    VerifyCalibration();
    VerifyTimedSeries();
    VerifyUndoAndClipboard();
    Console.WriteLine($"Portable analysis contract checks: PASS ({checks} assertions)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Portable analysis contract checks: FAIL");
    Console.Error.WriteLine(ex);
    return 1;
}

void VerifyCalibration()
{
    var calibration = PlanarCalibration.FromQuadrilateral(
        new Vector2(10, 20), new Vector2(210, 10), new Vector2(230, 130), new Vector2(0, 140), 20, 10);
    calibration.Validate();
    Equal(new Vector2(0, 10), calibration.ImageToWorld(calibration.A), "top-left maps above bottom-left origin");
    Equal(new Vector2(20, 10), calibration.ImageToWorld(calibration.B), "top-right maps to world width");
    Equal(new Vector2(20, 0), calibration.ImageToWorld(calibration.C), "bottom-right maps to world corner");
    Equal(new Vector2(0, 0), calibration.ImageToWorld(calibration.D), "bottom-left is world origin");

    var perspective = PlanarCalibration.FromQuadrilateral(
        new Vector2(10, 10), new Vector2(110, 10), new Vector2(90, 90), new Vector2(30, 90), 10, 10);
    Near(new Vector2(5, 5), perspective.ImageToWorld(new Vector2(60, 60)), 1e-4f,
        "trapezoid diagonal intersection maps to rectangle center");

    // Ground truth from the authored rectangle: center maps to its center in world coordinates.
    var rectangle = PlanarCalibration.FromQuadrilateral(
        Vector2.Zero, new Vector2(100, 0), new Vector2(100, 50), new Vector2(0, 50), 10, 5);
    Near(new Vector2(5, 2.5f), rectangle.ImageToWorld(new Vector2(50, 25)), 1e-4f, "rectangle center ground truth");
    Vector2 probe = new(61.25f, 31.5f);
    Near(probe, rectangle.WorldToImage(rectangle.ImageToWorld(probe)), 1e-3f, "image/world roundtrip");
    var persisted = JsonSerializer.Deserialize<PlanarCalibration>(JsonSerializer.Serialize(calibration, PortableAnalysisJson.Options), PortableAnalysisJson.Options);
    True(persisted is not null, "calibration JSON reopen");
    Near(calibration.ImageToWorld(new Vector2(83, 49)), persisted!.ImageToWorld(new Vector2(83, 49)), 1e-4f, "persisted calibration transform");
    Throws<InvalidOperationException>(() => PlanarCalibration.FromQuadrilateral(
        Vector2.Zero, Vector2.UnitX, new Vector2(2, 0), new Vector2(3, 0), 10, 5).ImageToWorld(Vector2.Zero), "degenerate quad rejected");
    Throws<InvalidOperationException>(() => PlanarCalibration.FromQuadrilateral(
        Vector2.Zero, new Vector2(2, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 2), 10, 5).Validate(), "concave quad rejected");
    Throws<InvalidOperationException>(() => PlanarCalibration.FromQuadrilateral(
        Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, 0, 5).Validate(), "zero world width rejected");
}

void VerifyTimedSeries()
{
    var series = new TimedSeries<Vector2>();
    series.Set(200, new Vector2(20, 40));
    series.Set(100, new Vector2(10, 20));
    series.Set(300, new Vector2(30, 60));
    Equal(100L, series.Samples[0].Timestamp, "samples sorted by timestamp");
    True(series.TryGetExact(200, out Vector2 exact), "exact timestamp found");
    Equal(new Vector2(20, 40), exact, "exact value");
    Equal(new Vector2(15, 30), series.InterpolateVector2(150), "linear interpolation");
    Equal(new Vector2(10, 20), series.InterpolateVector2(0), "clamps before first sample");
    Equal(new Vector2(30, 60), series.InterpolateVector2(400), "clamps after last sample");
    series.Set(200, new Vector2(22, 44));
    Equal(3, series.Samples.Count, "same timestamp replaces sample");
    True(series.TryGetExact(200, out exact), "replaced timestamp found");
    Equal(new Vector2(22, 44), exact, "replacement value");
    Throws<InvalidOperationException>(() => new TimedSeries<int>().InterpolateVector2(1), "empty series rejected");
    Throws<InvalidOperationException>(() => { var ints = new TimedSeries<int>(); ints.Set(1, 2); ints.InterpolateVector2(1); }, "non-vector series rejected");
}

void VerifyUndoAndClipboard()
{
    var history = new UndoHistory<string>("initial");
    True(!history.Undo(), "undo on empty history is a no-op");
    history.Push("one", "Add drawing");
    history.Push("two", "Move drawing");
    Equal("Move drawing", history.UndoName, "undo label");
    True(history.Undo(), "undo succeeds");
    Equal("one", history.Current, "undo restores previous state");
    Equal("Move drawing", history.RedoName, "redo label");
    True(history.Redo(), "redo succeeds");
    Equal("two", history.Current, "redo restores next state");
    True(history.Undo(), "second undo succeeds");
    history.Push("branch", "Edit drawing");
    True(!history.CanRedo, "new change clears redo history");
    var clipboard = new DrawingClipboardPayload("<Drawing />", new Vector2(3, 4), "Marker");
    var reopened = JsonSerializer.Deserialize<DrawingClipboardPayload>(JsonSerializer.Serialize(clipboard, PortableAnalysisJson.Options), PortableAnalysisJson.Options);
    Equal(clipboard, reopened, "clipboard payload roundtrip");
    Throws<ArgumentException>(() => new DrawingClipboardPayload("", Vector2.Zero, ""), "empty clipboard serialization rejected");
}

void True(bool condition, string label) { checks++; if (!condition) throw new InvalidOperationException($"Failed: {label}"); }
void Equal<T>(T expected, T actual, string label) { checks++; if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Failed: {label}; expected {expected}, got {actual}"); }
void Near(Vector2 expected, Vector2 actual, float tolerance, string label) { checks++; if (Vector2.Distance(expected, actual) > tolerance) throw new InvalidOperationException($"Failed: {label}; expected {expected}, got {actual}"); }
void Throws<T>(Action action, string label) where T : Exception { checks++; try { action(); } catch (T) { return; } throw new InvalidOperationException($"Failed: {label}; expected {typeof(T).Name}"); }
