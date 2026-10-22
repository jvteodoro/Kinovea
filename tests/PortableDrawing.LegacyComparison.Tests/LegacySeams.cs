// GeometryHelper has a historical using directive for log4net but does not use a log4net type.
// The empty namespace lets the unmodified source compile in this narrow characterization harness.
namespace log4net { }

namespace Kinovea.ScreenManager
{
    // These are the three helper members GeometryHelper needs outside the tested
    // distance/angle paths. They preserve their straightforward legacy meanings so
    // the linked source can compile without dragging in UI projects.
    internal static class MathHelper
    {
        public static float Radians(float degrees) => degrees * (float)Math.PI / 180f;
    }

    internal static class GeometryComparisonExtensions
    {
        public static System.Drawing.PointF Translate(this System.Drawing.PointF point, float x, float y) => new(point.X + x, point.Y + y);
        public static System.Drawing.PointF Center(this System.Drawing.RectangleF rectangle) => new(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
    }
}
