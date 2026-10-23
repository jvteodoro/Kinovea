// GeometryHelper has a historical using directive for log4net but does not use a log4net type.
// The empty namespace lets the unmodified source compile in this narrow characterization harness.
namespace log4net
{
    public interface ILog
    {
        void Debug(object message);
        void DebugFormat(string format, params object[] args);
    }
    public static class LogManager
    {
        public static ILog GetLogger(System.Type type) => new NullLog();
        private sealed class NullLog : ILog
        {
            public void Debug(object message) { }
            public void DebugFormat(string format, params object[] args) { }
        }
    }
}

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

    // `ProjectiveMapper` only uses the vertex/indexer surface. Production
    // QuadrilateralF also owns GDI+ hit-testing and rendering behavior.
    public sealed class QuadrilateralF
    {
        private readonly System.Drawing.PointF[] points;
        public System.Drawing.PointF A => points[0];
        public System.Drawing.PointF B => points[1];
        public System.Drawing.PointF C => points[2];
        public System.Drawing.PointF D => points[3];
        public System.Drawing.PointF this[int index] => points[index];
        public QuadrilateralF(System.Drawing.PointF a, System.Drawing.PointF b, System.Drawing.PointF c, System.Drawing.PointF d) => points = [a, b, c, d];
    }

    // A minimal stand-in for the class hierarchy that concrete legacy mementos use.
    // HistoryStack itself is linked from production source and is not substituted.
    public abstract class HistoryMemento
    {
        public abstract string CommandName { get; set; }
        public abstract HistoryMemento PerformUndo();
    }

    public abstract class HistoryMementoModifyDrawing : HistoryMemento
    {
        public abstract bool IsSameState(HistoryMementoModifyDrawing other);
    }
}
