using System.Numerics;

namespace Kinovea.PortableDrawing;

public enum OverlayHitKind
{
    Miss,
    Body,
    Handle
}

public readonly record struct OverlayHit(OverlayHitKind Kind, int Handle = 0)
{
    public static OverlayHit Miss => new(OverlayHitKind.Miss);
    public static OverlayHit Body => new(OverlayHitKind.Body);
    public static OverlayHit AtHandle(int handle) => new(OverlayHitKind.Handle, handle);
}

/// <summary>A drawing model in image coordinates, independent of any rendering toolkit.</summary>
public interface IOverlay
{
    Guid Id { get; }
    Vector2 CopyPoint { get; }
    OverlayHit HitTest(Vector2 point, float tolerance);
    bool MoveHandle(int handle, Vector2 point);
    void MoveBy(Vector2 delta);
}

public sealed class OverlayCollection
{
    private readonly List<IOverlay> overlays = [];

    public IReadOnlyList<IOverlay> Overlays => overlays;
    public Guid? SelectedId { get; private set; }
    public OverlayHit SelectedHit { get; private set; } = OverlayHit.Miss;

    public void Add(IOverlay overlay) => overlays.Add(overlay ?? throw new ArgumentNullException(nameof(overlay)));

    public bool Remove(Guid id)
    {
        int index = overlays.FindIndex(item => item.Id == id);
        if (index < 0)
            return false;

        overlays.RemoveAt(index);
        if (SelectedId == id)
            ClearSelection();
        return true;
    }

    /// <summary>Searches in collection order; the legacy drawing manager selects the first hit.</summary>
    public OverlayHit SelectAt(Vector2 point, float tolerance)
    {
        for (int i = 0; i < overlays.Count; i++)
        {
            OverlayHit hit = overlays[i].HitTest(point, tolerance);
            if (hit.Kind == OverlayHitKind.Miss)
                continue;

            SelectedId = overlays[i].Id;
            SelectedHit = hit;
            return hit;
        }

        ClearSelection();
        return OverlayHit.Miss;
    }

    public bool MoveSelectedHandle(Vector2 point)
    {
        IOverlay? selected = FindSelected();
        if (selected is null || SelectedHit.Kind != OverlayHitKind.Handle)
            return false;

        return selected.MoveHandle(SelectedHit.Handle, point);
    }

    public bool MoveSelected(Vector2 delta)
    {
        IOverlay? selected = FindSelected();
        if (selected is null || SelectedHit.Kind != OverlayHitKind.Body)
            return false;

        selected.MoveBy(delta);
        return true;
    }

    public void ClearSelection()
    {
        SelectedId = null;
        SelectedHit = OverlayHit.Miss;
    }

    private IOverlay? FindSelected() => SelectedId is Guid id ? overlays.FirstOrDefault(item => item.Id == id) : null;
}

public sealed class LineOverlay : IOverlay
{
    public Guid Id { get; } = Guid.NewGuid();
    public Vector2 A { get; private set; }
    public Vector2 B { get; private set; }
    public Vector2 CopyPoint => (A + B) / 2;

    public LineOverlay(Vector2 a, Vector2 b)
    {
        A = a;
        B = b;
    }

    public OverlayHit HitTest(Vector2 point, float tolerance)
    {
        if (tolerance < 0 || !float.IsFinite(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (Vector2.Distance(point, A) <= tolerance)
            return OverlayHit.AtHandle(1);
        if (Vector2.Distance(point, B) <= tolerance)
            return OverlayHit.AtHandle(2);
        return Geometry.DistanceToSegment(point, A, B) <= tolerance ? OverlayHit.Body : OverlayHit.Miss;
    }

    public bool MoveHandle(int handle, Vector2 point)
    {
        if (handle == 1) A = point;
        else if (handle == 2) B = point;
        else return false;
        return true;
    }

    public void MoveBy(Vector2 delta)
    {
        A += delta;
        B += delta;
    }
}

public sealed class AngleOverlay : IOverlay
{
    public Guid Id { get; } = Guid.NewGuid();
    public Vector2 Origin { get; private set; }
    public Vector2 A { get; private set; }
    public Vector2 B { get; private set; }
    public Vector2 CopyPoint => Origin;

    public AngleOverlay(Vector2 origin, Vector2 a, Vector2 b)
    {
        Origin = origin;
        A = a;
        B = b;
    }

    public OverlayHit HitTest(Vector2 point, float tolerance)
    {
        if (tolerance < 0 || !float.IsFinite(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (Vector2.Distance(point, Origin) <= tolerance) return OverlayHit.AtHandle(1);
        if (Vector2.Distance(point, A) <= tolerance) return OverlayHit.AtHandle(2);
        if (Vector2.Distance(point, B) <= tolerance) return OverlayHit.AtHandle(3);
        return Math.Min(Geometry.DistanceToSegment(point, Origin, A), Geometry.DistanceToSegment(point, Origin, B)) <= tolerance
            ? OverlayHit.Body : OverlayHit.Miss;
    }

    public bool MoveHandle(int handle, Vector2 point)
    {
        if (handle == 1) Origin = point;
        else if (handle == 2) A = point;
        else if (handle == 3) B = point;
        else return false;
        return true;
    }

    public void MoveBy(Vector2 delta)
    {
        Origin += delta;
        A += delta;
        B += delta;
    }
}

public sealed class TimeSegmentOverlay : IOverlay
{
    public Guid Id { get; } = Guid.NewGuid();
    public Vector2 A { get; private set; }
    public Vector2 B { get; private set; }
    public float Fraction { get; private set; }
    public long FrameStartTimestamp { get; }
    public long FrameIntervalTimestamps { get; }
    public long MeasuredTimestamp => checked(FrameStartTimestamp + (long)Math.Round(FrameIntervalTimestamps * (double)Fraction));
    public Vector2 FractionPoint => Vector2.Lerp(A, B, Fraction);
    public Vector2 CopyPoint => (A + B) / 2;

    public TimeSegmentOverlay(Vector2 a, Vector2 b, long frameStartTimestamp, long frameIntervalTimestamps, float fraction = 0.5f)
    {
        if (frameIntervalTimestamps < 0) throw new ArgumentOutOfRangeException(nameof(frameIntervalTimestamps));
        A = a;
        B = b;
        FrameStartTimestamp = frameStartTimestamp;
        FrameIntervalTimestamps = frameIntervalTimestamps;
        SetFraction(fraction);
    }

    public OverlayHit HitTest(Vector2 point, float tolerance)
    {
        if (tolerance < 0 || !float.IsFinite(tolerance))
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (Vector2.Distance(point, FractionPoint) <= tolerance) return OverlayHit.AtHandle(3);
        if (Vector2.Distance(point, A) <= tolerance) return OverlayHit.AtHandle(1);
        if (Vector2.Distance(point, B) <= tolerance) return OverlayHit.AtHandle(2);
        return Geometry.DistanceToSegment(point, A, B) <= tolerance ? OverlayHit.Body : OverlayHit.Miss;
    }

    public bool MoveHandle(int handle, Vector2 point)
    {
        if (handle == 1) A = point;
        else if (handle == 2) B = point;
        else if (handle == 3)
        {
            Vector2 direction = B - A;
            float lengthSquared = direction.LengthSquared();
            if (lengthSquared == 0) return false;
            SetFraction(Vector2.Dot(point - A, direction) / lengthSquared);
        }
        else return false;
        if (handle is 1 or 2 && Vector2.Distance(A, B) < 10)
            B = A + new Vector2(10, 0);
        return true;
    }

    public void MoveBy(Vector2 delta)
    {
        A += delta;
        B += delta;
    }

    private void SetFraction(float fraction)
    {
        if (!float.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        Fraction = Math.Clamp(fraction, 0, 1);
    }
}
