using System.Numerics;

namespace Kinovea.PortableDrawing;

/// <summary>Maps an image-space point into the calibrated measurement plane.</summary>
public interface IPointCalibration
{
    Vector2 Map(Vector2 imagePoint);
}

public sealed class IdentityCalibration : IPointCalibration
{
    public static IdentityCalibration Instance { get; } = new();
    private IdentityCalibration() { }
    public Vector2 Map(Vector2 imagePoint) => imagePoint;
}

public sealed class AffineCalibration : IPointCalibration
{
    private readonly Matrix3x2 transform;
    public AffineCalibration(Matrix3x2 transform) => this.transform = transform;
    public Vector2 Map(Vector2 imagePoint) => Vector2.Transform(imagePoint, transform);
}

public static class Measurements
{
    /// <summary>Measures the straight segment after mapping both endpoints, as legacy GetLengthText does.</summary>
    public static float Distance(Vector2 a, Vector2 b, IPointCalibration? calibration = null)
    {
        calibration ??= IdentityCalibration.Instance;
        return Vector2.Distance(calibration.Map(a), calibration.Map(b));
    }

    /// <summary>
    /// Returns the legacy GeometryHelper angle convention: atan2(cross(ab, ac), dot(ab, ac)),
    /// in radians within [-π, π].
    /// </summary>
    public static float AngleRadians(Vector2 origin, Vector2 a, Vector2 b, bool signed = true, bool counterClockwise = true, bool supplementary = false, IPointCalibration? calibration = null)
    {
        calibration ??= IdentityCalibration.Instance;
        if (supplementary)
        {
            Vector2 oppositeA = 2 * origin - a;
            a = b;
            b = oppositeA;
        }

        Vector2 o = calibration.Map(origin);
        Vector2 first = calibration.Map(counterClockwise ? a : b) - o;
        Vector2 second = calibration.Map(counterClockwise ? b : a) - o;
        float angle = MathF.Atan2(first.X * second.Y - first.Y * second.X, Vector2.Dot(first, second));
        return !signed && angle < 0 ? MathF.Tau + angle : angle;
    }

    public static float AngleDegrees(Vector2 origin, Vector2 a, Vector2 b, bool signed = true, bool counterClockwise = true, bool supplementary = false, IPointCalibration? calibration = null)
        => AngleRadians(origin, a, b, signed, counterClockwise, supplementary, calibration) * (180f / MathF.PI);
}

internal static class Geometry
{
    public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared == 0) return Vector2.Distance(point, a);
        float fraction = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0, 1);
        return Vector2.Distance(point, a + fraction * ab);
    }
}
