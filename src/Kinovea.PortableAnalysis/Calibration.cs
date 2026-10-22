using System.Numerics;

namespace Kinovea.PortableAnalysis;

/// <summary>
/// A serializable four-corner planar calibration. Coordinates use image space (+Y down)
/// and world space (+Y up). Origin and offset are expressed in world units.
/// </summary>
public sealed record PlanarCalibration(
    Vector2 A,
    Vector2 B,
    Vector2 C,
    Vector2 D,
    float WorldWidth,
    float WorldHeight,
    Vector2 Origin,
    Vector2 Offset)
{
    public static PlanarCalibration FromQuadrilateral(
        Vector2 a, Vector2 b, Vector2 c, Vector2 d, float worldWidth, float worldHeight) =>
        new(a, b, c, d, worldWidth, worldHeight, new Vector2(0, worldHeight), Vector2.Zero);

    public Vector2 ImageToWorld(Vector2 imagePoint)
    {
        Validate();
        var grid = Map(imagePoint, A, B, C, D, WorldWidth, WorldHeight);
        return new Vector2(grid.X - Origin.X + Offset.X, Origin.Y - grid.Y + Offset.Y);
    }

    public Vector2 WorldToImage(Vector2 worldPoint)
    {
        Validate();
        var grid = new Vector2(Origin.X + worldPoint.X - Offset.X, Origin.Y - worldPoint.Y + Offset.Y);
        return Map(grid,
            Vector2.Zero, new Vector2(WorldWidth, 0), new Vector2(WorldWidth, WorldHeight), new Vector2(0, WorldHeight),
            A, B, C, D);
    }

    public void Validate()
    {
        if (!float.IsFinite(WorldWidth) || !float.IsFinite(WorldHeight) || WorldWidth <= 0 || WorldHeight <= 0)
            throw new InvalidOperationException("Calibration dimensions must be finite and positive.");
        if (!IsFinite(A) || !IsFinite(B) || !IsFinite(C) || !IsFinite(D) || !IsFinite(Origin) || !IsFinite(Offset))
            throw new InvalidOperationException("Calibration coordinates must be finite.");
        if (!IsConvex(A, B, C, D))
            throw new InvalidOperationException("Calibration quadrilateral must be strictly convex.");
    }

    // Solve an 8-unknown homography with h22 fixed to one. The four source corners
    // correspond to the four target corners in order.
    private static Vector2 Map(Vector2 p, Vector2 s0, Vector2 s1, Vector2 s2, Vector2 s3,
        float width, float height) => Map(p, s0, s1, s2, s3,
            Vector2.Zero, new Vector2(width, 0), new Vector2(width, height), new Vector2(0, height));

    private static Vector2 Map(Vector2 p, Vector2 s0, Vector2 s1, Vector2 s2, Vector2 s3,
        Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3)
    {
        Span<double> matrix = stackalloc double[8 * 9];
        AddPair(matrix, 0, s0, t0); AddPair(matrix, 2, s1, t1);
        AddPair(matrix, 4, s2, t2); AddPair(matrix, 6, s3, t3);
        double[] h = Solve(matrix);
        double divisor = h[6] * p.X + h[7] * p.Y + 1;
        if (Math.Abs(divisor) < 1e-12)
            throw new InvalidOperationException("Point maps to infinity under this calibration.");
        return new Vector2((float)((h[0] * p.X + h[1] * p.Y + h[2]) / divisor),
            (float)((h[3] * p.X + h[4] * p.Y + h[5]) / divisor));
    }

    private static void AddPair(Span<double> m, int row, Vector2 s, Vector2 t)
    {
        int x = row * 9, y = (row + 1) * 9;
        m[x] = s.X; m[x + 1] = s.Y; m[x + 2] = 1; m[x + 6] = -t.X * s.X; m[x + 7] = -t.X * s.Y; m[x + 8] = t.X;
        m[y + 3] = s.X; m[y + 4] = s.Y; m[y + 5] = 1; m[y + 6] = -t.Y * s.X; m[y + 7] = -t.Y * s.Y; m[y + 8] = t.Y;
    }

    private static double[] Solve(Span<double> m)
    {
        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < 8; row++)
                if (Math.Abs(m[row * 9 + col]) > Math.Abs(m[pivot * 9 + col])) pivot = row;
            if (Math.Abs(m[pivot * 9 + col]) < 1e-12) throw new InvalidOperationException("Calibration quadrilateral is singular.");
            for (int c = col; c < 9; c++) (m[col * 9 + c], m[pivot * 9 + c]) = (m[pivot * 9 + c], m[col * 9 + c]);
            double scale = m[col * 9 + col];
            for (int c = col; c < 9; c++) m[col * 9 + c] /= scale;
            for (int row = 0; row < 8; row++)
            {
                if (row == col) continue;
                double factor = m[row * 9 + col];
                for (int c = col; c < 9; c++) m[row * 9 + c] -= factor * m[col * 9 + c];
            }
        }
        var result = new double[8];
        for (int row = 0; row < 8; row++) result[row] = m[row * 9 + 8];
        return result;
    }

    private static bool IsFinite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
    private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;
    private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        double ab = Cross(b - a, c - b), bc = Cross(c - b, d - c);
        double cd = Cross(d - c, a - d), da = Cross(a - d, b - a);
        const double epsilon = 1e-6;
        return (ab > epsilon && bc > epsilon && cd > epsilon && da > epsilon) ||
            (ab < -epsilon && bc < -epsilon && cd < -epsilon && da < -epsilon);
    }
}
