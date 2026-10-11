using System.Drawing;
using Kinovea.ScreenManager;

var checks = new Checks();
var uninitialized = new ProjectiveMapper();
checks.Throws<NullReferenceException>(() => uninitialized.Forward(new PointF(1, 1)), "default mapper has no identity initialization");
var plane = new QuadrilateralF(new PointF(0, 0), new PointF(2, 0), new PointF(2, 2), new PointF(0, 2));
var image = new QuadrilateralF(new PointF(10, 20), new PointF(14, 20), new PointF(12, 24), new PointF(8, 24));
var mapper = new ProjectiveMapper(plane, image);

checks.Point(new PointF(10, 20), mapper.Forward(new PointF(0, 0)), "forward A vertex");
checks.Point(new PointF(14, 20), mapper.Forward(new PointF(2, 0)), "forward B vertex");
checks.Point(new PointF(12, 24), mapper.Forward(new PointF(2, 2)), "forward C vertex");
checks.Point(new PointF(8, 24), mapper.Forward(new PointF(0, 2)), "forward D vertex");
var interior = new PointF(0.7f, 1.1f);
checks.Point(interior, mapper.Backward(mapper.Forward(interior)), "point round trip");

var forwardQuad = mapper.Forward(plane);
checks.Point(image.A, forwardQuad.A, "quadrilateral overload A");
checks.Point(image.B, forwardQuad.B, "quadrilateral overload B");
checks.Point(image.C, forwardQuad.C, "quadrilateral overload C");
checks.Point(image.D, forwardQuad.D, "quadrilateral overload D");
var backwardQuad = mapper.Backward(image);
checks.Point(plane.A, backwardQuad.A, "backward quadrilateral overload A");
checks.Point(plane.B, backwardQuad.B, "backward quadrilateral overload B");
checks.Point(plane.C, backwardQuad.C, "backward quadrilateral overload C");
checks.Point(plane.D, backwardQuad.D, "backward quadrilateral overload D");

var homogeneous = mapper.Forward(new Vector3(interior.X, interior.Y, 1));
var homogeneousBack = mapper.Backward(homogeneous);
checks.Near(3161.6f, homogeneousBack.Y, 0.01f, "homogeneous backward Y pins current mapMatrix coefficient quirk");

var invalidOutput = new QuadrilateralF(new PointF(0, 0), new PointF(1, 0), new PointF(2, 0), new PointF(3, 0));
mapper.Update(plane, invalidOutput);
checks.Point(new PointF(11, 22), mapper.Forward(new PointF(1, 1)), "invalid output update preserves previous mapping");
var invalidPlane = new QuadrilateralF(new PointF(0, 0), new PointF(2, 0), new PointF(1, 1), new PointF(0, 2));
checks.Throws<NullReferenceException>(() => mapper.Update(invalidPlane, image), "invalid input quadrilateral throws during matrix composition");

var affinePlane = new QuadrilateralF(new PointF(0, 0), new PointF(2, 0), new PointF(2, 2), new PointF(0, 2));
var affineImage = new QuadrilateralF(new PointF(3, 4), new PointF(7, 4), new PointF(7, 10), new PointF(3, 10));
var affine = new ProjectiveMapper(affinePlane, affineImage);
checks.Point(new PointF(5, 7), affine.Forward(new PointF(1, 1)), "affine interior mapping");
checks.Point(new PointF(1, 1), affine.Backward(new PointF(5, 7)), "affine reverse mapping");
var ellipse = affine.Ellipse();
checks.Point(new PointF(3, 4), ellipse.Center, "ellipse center");
checks.Near(3f, ellipse.SemiMajorAxis, 1e-5f, "ellipse semi-major axis");
checks.Near(2f, ellipse.SemiMinorAxis, 1e-5f, "ellipse semi-minor axis");
checks.Near(MathF.PI / 2, ellipse.Rotation, 1e-5f, "ellipse rotation");

Console.WriteLine($"PASS: {checks.Count} ProjectiveMapper characterization assertions");

sealed class Checks
{
    public int Count { get; private set; }
    public void True(bool value, string name)
    {
        Count++;
        if (!value) throw new InvalidOperationException(name);
    }
    public void Throws<TException>(Action action, string name) where TException : Exception
    {
        Count++;
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
    }
    public void Point(PointF expected, PointF actual, string name)
    {
        Near(expected.X, actual.X, name + " X");
        Near(expected.Y, actual.Y, name + " Y");
    }
    public void Near(float expected, float actual, float tolerance, string name)
    {
        Count++;
        if (float.IsNaN(actual) || MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
    private void Near(float expected, float actual, string name) => Near(expected, actual, 1e-5f, name);
}

// ProjectiveMapper only constructs this logger; its mapping paths do not use it.
namespace log4net
{
    public interface ILog { }
    public static class LogManager
    {
        public static ILog GetLogger(System.Type type) => new NullLog();
        private sealed class NullLog : ILog { }
    }
}

namespace Kinovea.ScreenManager
{
    // Narrow test seam for the mapper's vertex/indexer contract. The production
    // QuadrilateralF contains GDI+ drawing operations, which this suite does not call.
    public sealed class QuadrilateralF
    {
        private readonly PointF[] points;
        public PointF A => points[0];
        public PointF B => points[1];
        public PointF C => points[2];
        public PointF D => points[3];
        public PointF this[int index] => points[index];
        public QuadrilateralF(PointF a, PointF b, PointF c, PointF d) => points = [a, b, c, d];
    }
}
