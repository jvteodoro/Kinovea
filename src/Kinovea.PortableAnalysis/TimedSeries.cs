using System.Numerics;

namespace Kinovea.PortableAnalysis;

public readonly record struct TimedValue<T>(long Timestamp, T Value);

/// <summary>Sorted timestamp samples with exact lookup and explicit linear interpolation.</summary>
public sealed class TimedSeries<T> where T : struct
{
    private readonly List<TimedValue<T>> samples = [];
    public IReadOnlyList<TimedValue<T>> Samples => samples;

    public void Set(long timestamp, T value)
    {
        int index = samples.BinarySearch(new TimedValue<T>(timestamp, default), TimestampComparer<T>.Instance);
        if (index >= 0) samples[index] = new TimedValue<T>(timestamp, value);
        else samples.Insert(~index, new TimedValue<T>(timestamp, value));
    }

    public bool TryGetExact(long timestamp, out T value)
    {
        int index = samples.BinarySearch(new TimedValue<T>(timestamp, default), TimestampComparer<T>.Instance);
        if (index >= 0) { value = samples[index].Value; return true; }
        value = default; return false;
    }

    public Vector2 InterpolateVector2(long timestamp)
    {
        if (typeof(T) != typeof(Vector2)) throw new InvalidOperationException("Linear vector interpolation requires Vector2 samples.");
        if (samples.Count == 0) throw new InvalidOperationException("Cannot interpolate an empty series.");
        int upper = samples.FindIndex(s => s.Timestamp >= timestamp);
        if (upper == 0) return (Vector2)(object)samples[0].Value;
        if (upper < 0) return (Vector2)(object)samples[^1].Value;
        var left = samples[upper - 1]; var right = samples[upper];
        if (right.Timestamp == timestamp) return (Vector2)(object)right.Value;
        double amount = ((double)timestamp - left.Timestamp) / ((double)right.Timestamp - left.Timestamp);
        return Vector2.Lerp((Vector2)(object)left.Value, (Vector2)(object)right.Value, (float)amount);
    }

    private sealed class TimestampComparer<TValue> : IComparer<TimedValue<TValue>>
    {
        public static readonly TimestampComparer<TValue> Instance = new();
        public int Compare(TimedValue<TValue> x, TimedValue<TValue> y) => x.Timestamp.CompareTo(y.Timestamp);
    }
}
