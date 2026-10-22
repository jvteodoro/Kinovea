using System.Drawing;
using System.Numerics;
using Kinovea.PortableDrawing;
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
