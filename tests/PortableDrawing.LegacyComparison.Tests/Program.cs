using System.Drawing;
using System.Numerics;
using Kinovea.PortableDrawing;
using Kinovea.PortableAnalysis;
using LegacyGeometry = Kinovea.ScreenManager.GeometryHelper;

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

Console.WriteLine($"PASS: {checks.Count} legacy-to-portable drawing arithmetic comparison assertions");

static PointF ToPoint(Vector2 value) => new(value.X, value.Y);

sealed class Checks
{
    public int Count { get; private set; }
    public void Near(float expected, float actual, string name)
    {
        Count++;
        if (MathF.Abs(expected - actual) > 0.00001f)
            throw new InvalidOperationException($"{name}: legacy <{expected:R}>, portable <{actual:R}>");
    }
}
