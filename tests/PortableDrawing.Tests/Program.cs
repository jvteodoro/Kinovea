using System.Numerics;
using Kinovea.PortableDrawing;

var checks = new Checks();

var line = new LineOverlay(new Vector2(0, 0), new Vector2(10, 0));
checks.Equal(10f, Measurements.Distance(line.A, line.B), "uncalibrated line distance is in image units");
checks.Equal(20f, Measurements.Distance(line.A, line.B, new AffineCalibration(Matrix3x2.CreateScale(2, 1))), "distance maps both endpoints before measuring");
checks.Equal(new Vector2(5, 0), line.CopyPoint, "line copy point is midpoint");
checks.Equal(OverlayHit.AtHandle(1), line.HitTest(new Vector2(1, 0), 1), "line handle priority");
checks.Equal(OverlayHit.Body, line.HitTest(new Vector2(5, 1), 1), "line body hit at tolerance boundary");
checks.Equal(OverlayHit.Miss, line.HitTest(new Vector2(5, 1.01f), 1), "line misses outside tolerance");

checks.Equal(90f, Measurements.AngleDegrees(Vector2.Zero, Vector2.UnitX, Vector2.UnitY), "legacy angle orientation and degrees");
checks.Equal(-90f, Measurements.AngleDegrees(Vector2.Zero, Vector2.UnitX, -Vector2.UnitY), "signed negative angle");
checks.Equal(270f, Measurements.AngleDegrees(Vector2.Zero, Vector2.UnitX, -Vector2.UnitY, signed: false), "unsigned angle wraps to a full turn");
checks.Equal(-90f, Measurements.AngleDegrees(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, counterClockwise: false), "clockwise option swaps legs");
checks.Equal(90f, Measurements.AngleDegrees(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, supplementary: true), "supplementary angle uses opposite first leg");
checks.Equal(90f, Measurements.AngleDegrees(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, calibration: new AffineCalibration(Matrix3x2.CreateScale(1, 2))), "angle is computed after point calibration");

var angle = new AngleOverlay(Vector2.Zero, Vector2.UnitX, Vector2.UnitY);
checks.Equal(OverlayHit.AtHandle(1), angle.HitTest(new Vector2(0.5f, 0), 0.5f), "angle origin handle precedes arm body");
angle.MoveBy(new Vector2(2, 3));
checks.Equal(new Vector2(2, 3), angle.Origin, "moving angle translates origin");
checks.Equal(new Vector2(3, 3), angle.A, "moving angle translates first arm");

var segment = new TimeSegmentOverlay(new Vector2(0, 0), new Vector2(10, 0), 1000, 40);
checks.Equal(1020L, segment.MeasuredTimestamp, "time segment defaults to frame midpoint");
checks.Equal(OverlayHit.AtHandle(3), segment.HitTest(new Vector2(5, 0), 0), "time fraction handle is prioritized");
checks.True(segment.MoveHandle(3, new Vector2(7.5f, 2)), "time fraction handle moves");
checks.Equal(0.75f, segment.Fraction, "time fraction projects and clamps to the segment");
checks.Equal(1030L, segment.MeasuredTimestamp, "time measurement interpolates within timestamp interval");
checks.True(segment.MoveHandle(3, new Vector2(30, 0)), "time fraction can be moved beyond endpoint");
checks.Equal(1f, segment.Fraction, "time fraction clamps at endpoint");
checks.Equal(1040L, segment.MeasuredTimestamp, "clamped time measurement reaches next frame timestamp");
var shortSegment = new TimeSegmentOverlay(Vector2.Zero, new Vector2(10, 0), 0, 10);
checks.True(shortSegment.MoveHandle(2, new Vector2(5, 0)), "time segment endpoint movement");
checks.Equal(new Vector2(10, 0), shortSegment.B, "legacy time segment enforces a 10 image-unit minimum after handle movement");

var collection = new OverlayCollection();
var behind = new LineOverlay(new Vector2(0, 0), new Vector2(10, 0));
var front = new LineOverlay(new Vector2(0, 0), new Vector2(10, 0));
collection.Add(behind);
collection.Add(front);
checks.Equal(OverlayHit.AtHandle(1), collection.SelectAt(Vector2.Zero, 1), "selection hits first matching overlay");
checks.Equal(behind.Id, collection.SelectedId, "legacy collection order selects first matching overlay");
checks.True(collection.MoveSelectedHandle(new Vector2(-2, -1)), "selected handle movement");
checks.Equal(new Vector2(-2, -1), behind.A, "handle update is applied to selected overlay");
checks.Equal(new Vector2(0, 0), front.A, "handle update leaves the other overlay untouched");
checks.Equal(OverlayHit.Body, collection.SelectAt(new Vector2(5, 0), 0.1f), "selection identifies body hit after earlier overlay misses");
checks.True(collection.MoveSelected(new Vector2(2, 4)), "selected body movement");
checks.Equal(new Vector2(12, 4), front.B, "body movement translates selected endpoints");
checks.Equal(OverlayHit.Miss, collection.SelectAt(new Vector2(50, 50), 1), "miss clears selection");
checks.True(collection.SelectedId is null, "selection cleared after miss");
checks.True(collection.Remove(behind.Id), "overlay removal");

Console.WriteLine($"PASS {checks.Count} portable drawing assertions");

sealed class Checks
{
    public int Count { get; private set; }
    public void True(bool actual, string name)
    {
        Count++;
        if (!actual) throw new InvalidOperationException($"FAIL: {name}");
    }
    public void Equal<T>(T expected, T actual, string name)
    {
        Count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"FAIL: {name}; expected {expected}; got {actual}");
    }
}
