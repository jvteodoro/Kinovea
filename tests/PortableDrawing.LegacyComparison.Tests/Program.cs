using System.Drawing;
using System.Numerics;
using Kinovea.PortableDrawing;
using Kinovea.PortableAnalysis;
using LegacyGeometry = Kinovea.ScreenManager.GeometryHelper;
using LegacyHistory = Kinovea.ScreenManager.HistoryStack;
using LegacyMemento = Kinovea.ScreenManager.HistoryMemento;
using LegacyCalibratorPlane = Kinovea.ScreenManager.CalibratorPlane;
using LegacyTrackSelector = Kinovea.ScreenManager.LegacySourceExcerpt.LegacyDrawingTrackSelector;

// This intentionally links the unchanged legacy arithmetic source. It is an executable
// source-level comparison, not a Windows/.NET Framework or GUI observation.
var checks = new Checks();

var cases = new[]
{
    new { Name = "3-4-5 distance", A = new Vector2(1, 2), B = new Vector2(4, 6) },
    new { Name = "diagonal distance", A = new Vector2(-12.5f, 4.25f), B = new Vector2(9.5f, -7.75f) },
    new { Name = "zero distance", A = new Vector2(3, -2), B = new Vector2(3, -2) },
};

foreach (var item in cases)
{
    float legacy = LegacyGeometry.GetDistance(ToPoint(item.A), ToPoint(item.B));
    float portable = Measurements.Distance(item.A, item.B);
    checks.Near(legacy, portable, item.Name);
}

var angleCases = new[]
{
    new { Name = "right turn", Origin = Vector2.Zero, A = Vector2.UnitX, B = Vector2.UnitY, Signed = true, Ccw = true },
    new { Name = "negative right turn", Origin = Vector2.Zero, A = Vector2.UnitX, B = -Vector2.UnitY, Signed = true, Ccw = true },
    new { Name = "clockwise option", Origin = Vector2.Zero, A = Vector2.UnitX, B = Vector2.UnitY, Signed = true, Ccw = false },
    new { Name = "oblique angle", Origin = new Vector2(-5, 7), A = new Vector2(4, 8), B = new Vector2(-8, 13), Signed = true, Ccw = true },
};

foreach (var item in angleCases)
{
    float legacy = item.Ccw
        ? LegacyGeometry.GetAngle(ToPoint(item.Origin), ToPoint(item.A), ToPoint(item.B))
        : LegacyGeometry.GetAngle(ToPoint(item.Origin), ToPoint(item.B), ToPoint(item.A));
    float portable = Measurements.AngleRadians(item.Origin, item.A, item.B, item.Signed, item.Ccw);
    checks.Near(legacy, portable, item.Name);
}

// AngleHelper maps all three points before calling GeometryHelper. Compare that exact
// arithmetic boundary using an affine calibration whose mapping is independently stated.
var scale = Matrix3x2.CreateScale(2, 3);
var calibration = new AffineCalibration(scale);
var origin = new Vector2(1, 1);
var a = new Vector2(4, 1);
var b = new Vector2(1, 3);
float calibratedLegacy = LegacyGeometry.GetAngle(ToPoint(Vector2.Transform(origin, scale)),
    ToPoint(Vector2.Transform(a, scale)), ToPoint(Vector2.Transform(b, scale)));
checks.Near(calibratedLegacy, Measurements.AngleRadians(origin, a, b, calibration: calibration), "calibrated angle maps endpoints before arithmetic");

float calibratedDistanceLegacy = LegacyGeometry.GetDistance(ToPoint(Vector2.Transform(origin, scale)), ToPoint(Vector2.Transform(a, scale)));
checks.Near(calibratedDistanceLegacy, Measurements.Distance(origin, a, calibration), "calibrated distance maps endpoints before arithmetic");

float signedNegative = Measurements.AngleRadians(Vector2.Zero, Vector2.UnitX, -Vector2.UnitY);
float unsignedPortable = Measurements.AngleRadians(Vector2.Zero, Vector2.UnitX, -Vector2.UnitY, signed: false);
checks.Near((float)(Math.PI * 2) + signedNegative, unsignedPortable, "unsigned portable wrap follows legacy AngleHelper rule");

// Compare actual linked ProjectiveMapper backward mapping, followed by the legacy
// CalibratorPlane's documented D-origin/Y-up conversion, to the portable homography.
var imageCases = new[]
{
    new { Name = "axis-aligned calibration", A = new Vector2(10, 20), B = new Vector2(110, 20), C = new Vector2(110, 70), D = new Vector2(10, 70), Width = 2f, Height = 1f },
    new { Name = "projective trapezoid calibration", A = new Vector2(14, 8), B = new Vector2(126, 16), C = new Vector2(102, 91), D = new Vector2(28, 74), Width = 3f, Height = 2f },
};

foreach (var item in imageCases)
{
    var legacyPlane = new Kinovea.ScreenManager.QuadrilateralF(
        new PointF(0, 0), new PointF(item.Width, 0), new PointF(item.Width, item.Height), new PointF(0, item.Height));
    var legacyImage = new Kinovea.ScreenManager.QuadrilateralF(ToPoint(item.A), ToPoint(item.B), ToPoint(item.C), ToPoint(item.D));
    var mapper = new Kinovea.ScreenManager.ProjectiveMapper(legacyPlane, legacyImage);
    var portable = PlanarCalibration.FromQuadrilateral(item.A, item.B, item.C, item.D, item.Width, item.Height);

    foreach (var imagePoint in new[]
    {
        item.A,
        item.B,
        item.C,
        item.D,
        (item.A + item.B + item.C + item.D) / 4,
        Vector2.Lerp(item.A, item.C, 0.37f),
    })
    {
        PointF legacyGrid = mapper.Backward(ToPoint(imagePoint));
        var legacyWorld = new Vector2(legacyGrid.X, item.Height - legacyGrid.Y);
        checks.Near(legacyWorld.X, portable.ImageToWorld(imagePoint).X, item.Name + " X image-to-world");
        checks.Near(legacyWorld.Y, portable.ImageToWorld(imagePoint).Y, item.Name + " Y image-to-world");
    }
}

// Exercise the unchanged CalibratorPlane source itself, not only its ProjectiveMapper.
foreach (var item in imageCases)
{
    var imageQuad = new Kinovea.ScreenManager.QuadrilateralF(ToPoint(item.A), ToPoint(item.B), ToPoint(item.C), ToPoint(item.D));
    var legacy = new LegacyCalibratorPlane();
    legacy.Initialize(new SizeF(item.Width, item.Height), imageQuad);
    var portable = PlanarCalibration.FromQuadrilateral(item.A, item.B, item.C, item.D, item.Width, item.Height);
    var samplePoints = new[]
    {
        item.A,
        item.B,
        item.C,
        item.D,
        (item.A + item.B + item.C + item.D) / 4,
        Vector2.Lerp(item.A, item.C, 0.37f),
    };

    foreach (var point in samplePoints)
    {
        PointF actual = legacy.Transform(ToPoint(point));
        Vector2 expected = portable.ImageToWorld(point);
        checks.Near(actual.X, expected.X, item.Name + " CalibratorPlane X");
        checks.Near(actual.Y, expected.Y, item.Name + " CalibratorPlane Y");
    }

    legacy.Offset = new PointF(2.25f, -3.5f);
    var offsetPortable = portable with { Offset = new Vector2(2.25f, -3.5f) };
    PointF offsetActual = legacy.Transform(ToPoint(samplePoints[4]));
    Vector2 offsetExpected = offsetPortable.ImageToWorld(samplePoints[4]);
    checks.Near(offsetActual.X, offsetExpected.X, item.Name + " CalibratorPlane offset X");
    checks.Near(offsetActual.Y, offsetExpected.Y, item.Name + " CalibratorPlane offset Y");
}

// Compare legacy line-axis quadrilateral construction and the full CalibratorPlane
// transform against the portable planar mapping for each supported calibration axis.
foreach (var axis in Enum.GetValues<Kinovea.ScreenManager.CalibrationAxis>())
{
    const float lineLengthWorld = 5;
    PointF lineA = new(20, 20);
    PointF lineB = new(50, 40);
    var legacy = new LegacyCalibratorPlane();
    legacy.Initialize(lineLengthWorld, lineA, lineB, axis);
    var quad = legacy.QuadImage;
    var portable = PlanarCalibration.FromQuadrilateral(
        new Vector2(quad.A.X, quad.A.Y), new Vector2(quad.B.X, quad.B.Y),
        new Vector2(quad.C.X, quad.C.Y), new Vector2(quad.D.X, quad.D.Y),
        lineLengthWorld, lineLengthWorld);

    foreach (var point in new[]
    {
        new Vector2(lineA.X, lineA.Y),
        new Vector2(lineB.X, lineB.Y),
        new Vector2((lineA.X + lineB.X) / 2, (lineA.Y + lineB.Y) / 2),
    })
    {
        PointF actual = legacy.Transform(ToPoint(point));
        Vector2 expected = portable.ImageToWorld(point);
        checks.Near(actual.X, expected.X, $"line calibration {axis} X");
        checks.Near(actual.Y, expected.Y, $"line calibration {axis} Y");
    }

    PointF customOrigin = new(29, 26);
    legacy.SetOrigin(customOrigin);
    PointF customOriginGrid = legacy.Mapper.Backward(customOrigin);
    Vector2 portableOrigin = new(customOriginGrid.X, customOriginGrid.Y);
    var customOriginPortable = portable with { Origin = portableOrigin };
    PointF customActual = legacy.Transform(new PointF(37, 31));
    Vector2 customExpected = customOriginPortable.ImageToWorld(new Vector2(37, 31));
    checks.Near(customActual.X, customExpected.X, $"line calibration {axis} custom origin X");
    checks.Near(customActual.Y, customExpected.Y, $"line calibration {axis} custom origin Y");
}

// DrawingTimeSegment.MoveHandle(handle 3) uses the legacy closest-point helper,
// then derives the slider fraction from distances along AB. Compare that exact
// linked calculation and GetTimePoint's GeometryHelper.Mix with TimeSegmentOverlay.
// This covers fraction placement only; the drawing's metadata/time-label lifecycle
// and 10-pixel endpoint rule are outside this source-linked comparison.
var timeSegmentCases = new[]
{
    new { Name = "midpoint", A = new Vector2(10, 20), B = new Vector2(50, 20), Drag = new Vector2(30, 24) },
    new { Name = "before A clamps", A = new Vector2(10, 20), B = new Vector2(50, 20), Drag = new Vector2(-5, 20) },
    new { Name = "after B clamps", A = new Vector2(10, 20), B = new Vector2(50, 20), Drag = new Vector2(80, 20) },
    new { Name = "diagonal projection", A = new Vector2(-4, 8), B = new Vector2(20, 44), Drag = new Vector2(17, 24) },
};

foreach (var item in timeSegmentCases)
{
    PointF legacyA = ToPoint(item.A);
    PointF legacyB = ToPoint(item.B);
    PointF legacyDrag = ToPoint(item.Drag);
    PointF closest = LegacyGeometry.GetClosestPoint(legacyA, legacyB, legacyDrag,
        Kinovea.ScreenManager.PointLinePosition.OnSegment, 0);
    float legacyFraction = LegacyGeometry.GetDistance(legacyA, closest) / LegacyGeometry.GetDistance(legacyA, legacyB);

    var portable = new TimeSegmentOverlay(item.A, item.B, 0, 100);
    checks.True(portable.MoveHandle(3, item.Drag), $"time segment {item.Name} fraction handle moves");
    checks.Near(legacyFraction, portable.Fraction, $"time segment {item.Name} fraction");

    PointF legacyFractionPoint = LegacyGeometry.Mix(legacyA, legacyB, legacyFraction);
    checks.Near(legacyFractionPoint.X, portable.FractionPoint.X, $"time segment {item.Name} point X");
    checks.Near(legacyFractionPoint.Y, portable.FractionPoint.Y, $"time segment {item.Name} point Y");
}

// DrawingTrack's nearest-keyframe selector is compiled from a checked-in excerpt;
// the verifier command proves its method body matches production DrawingTrack.cs.
// TimedSeries has different, intentional interpolation semantics, so report those
// differences explicitly instead of treating interpolated values as nearest points.
var trackPoints = new List<Kinovea.Services.TimedPoint>
{
    new(10, 20, 100),
    new(20, 40, 200),
    new(30, 60, 300),
};
var trackSeries = new TimedSeries<Vector2>();
foreach (var point in trackPoints)
    trackSeries.Set(point.T, new Vector2(point.X, point.Y));

var trackSelectionCases = new[]
{
    new { Name = "before first", Timestamp = 50L, LegacyIndex = 0, PortableValue = new Vector2(10, 20), SameSelectedValue = true },
    new { Name = "between closer to first", Timestamp = 149L, LegacyIndex = 0, PortableValue = new Vector2(14.9f, 29.8f), SameSelectedValue = false },
    new { Name = "midpoint tie chooses first", Timestamp = 150L, LegacyIndex = 0, PortableValue = new Vector2(15, 30), SameSelectedValue = false },
    new { Name = "exact sample", Timestamp = 200L, LegacyIndex = 1, PortableValue = new Vector2(20, 40), SameSelectedValue = true },
    new { Name = "midpoint tie chooses earlier", Timestamp = 250L, LegacyIndex = 1, PortableValue = new Vector2(25, 50), SameSelectedValue = false },
    new { Name = "after last", Timestamp = 350L, LegacyIndex = 2, PortableValue = new Vector2(30, 60), SameSelectedValue = true },
};

foreach (var item in trackSelectionCases)
{
    int legacyIndex = LegacyTrackSelector.FindClosestPoint(item.Timestamp, trackPoints);
    Vector2 legacyValue = new(trackPoints[legacyIndex].X, trackPoints[legacyIndex].Y);
    Vector2 portableValue = trackSeries.InterpolateVector2(item.Timestamp);
    checks.True(legacyIndex == item.LegacyIndex, $"DrawingTrack selector {item.Name} index");
    checks.True(item.PortableValue == portableValue, $"TimedSeries {item.Name} interpolation/clamp");
    checks.True((legacyValue == portableValue) == item.SameSelectedValue,
        $"nearest keyframe versus TimedSeries {item.Name} scoped equivalence classification");
}

// Compare the legacy command stack's state transition sequence with the portable
// snapshot stack. The test mementos model the before/after action; they do not stand
// in for Kinovea's concrete drawing mementos or their UI side effects.
var legacyState = "initial";
var legacyHistory = new LegacyHistory();
var portableHistory = new UndoHistory<string>(legacyState);
int legacyHistoryChanged = 0;
legacyHistory.HistoryChanged += (_, _) => legacyHistoryChanged++;

legacyState = "one";
legacyHistory.PushNewCommand(new TestStateMemento("Add", "initial", () => legacyState, value => legacyState = value));
portableHistory.Push("one", "Add");
legacyState = "two";
legacyHistory.PushNewCommand(new TestStateMemento("Move", "one", () => legacyState, value => legacyState = value));
portableHistory.Push("two", "Move");
legacyHistory.StepBackward();
portableHistory.Undo();
checks.True(legacyState == portableHistory.Current && legacyState == "one", "legacy and portable undo restore the same scoped snapshot");
legacyHistory.StepForward();
portableHistory.Redo();
checks.True(legacyState == portableHistory.Current && legacyState == "two", "legacy and portable redo restore the same scoped snapshot");
legacyHistory.StepBackward();
portableHistory.Undo();
legacyState = "branch";
legacyHistory.PushNewCommand(new TestStateMemento("Branch", "one", () => legacyState, value => legacyState = value));
portableHistory.Push("branch", "Branch");
checks.True(legacyState == portableHistory.Current && !legacyHistory.CanRedo && !portableHistory.CanRedo, "legacy and portable new-branch push invalidates redo");
checks.True(legacyHistoryChanged == 7, "legacy HistoryStack event count includes separate redo clear and push notifications");

// Production HistoryStack suppresses consecutive modify-drawing mementos when
// IsSameState says they represent the same state. Portable UndoHistory has no such
// memento-aware policy; this test pins the source behavior and documents the gap.
var dedupLegacy = new LegacyHistory();
var dedupPortable = new UndoHistory<string>("base");
dedupLegacy.PushNewCommand(new TestModifyMemento("same"));
dedupLegacy.PushNewCommand(new TestModifyMemento("same"));
dedupPortable.Push("edit-1", "Edit");
dedupPortable.Push("edit-2", "Edit");
dedupLegacy.StepBackward();
checks.True(!dedupLegacy.CanUndo, "legacy stack discarded the consecutive duplicate modify memento");
checks.True(dedupPortable.Undo() && dedupPortable.CanUndo, "portable stack retains the second equal-payload push");
checks.True(dedupPortable.Undo() && !dedupPortable.CanUndo, "portable stack retains both pushes because it has no legacy deduplication policy");

Console.WriteLine($"PASS: {checks.Count} legacy-to-portable behavior comparison assertions");

static PointF ToPoint(Vector2 value) => new(value.X, value.Y);

sealed class Checks
{
    public int Count { get; private set; }
    public void True(bool actual, string name)
    {
        Count++;
        if (!actual)
            throw new InvalidOperationException(name);
    }
    public void Near(float expected, float actual, string name)
    {
        Count++;
        if (MathF.Abs(expected - actual) > 0.00001f)
            throw new InvalidOperationException($"{name}: legacy <{expected:R}>, portable <{actual:R}>");
    }
}

sealed class TestStateMemento : LegacyMemento
{
    private readonly string name;
    private readonly string target;
    private readonly Func<string> read;
    private readonly Action<string> write;

    public TestStateMemento(string name, string target, Func<string> read, Action<string> write)
    {
        this.name = name;
        this.target = target;
        this.read = read;
        this.write = write;
    }

    public override string CommandName { get; set; }
    public override LegacyMemento PerformUndo()
    {
        string current = read();
        write(target);
        return new TestStateMemento(name, current, read, write);
    }
}

sealed class TestModifyMemento : Kinovea.ScreenManager.HistoryMementoModifyDrawing
{
    private readonly string state;
    public TestModifyMemento(string state) => this.state = state;
    public override string CommandName { get; set; } = "Modify drawing";
    public override LegacyMemento PerformUndo() => this;
    public override bool IsSameState(Kinovea.ScreenManager.HistoryMementoModifyDrawing other) =>
        other is TestModifyMemento modify && modify.state == state;
}
