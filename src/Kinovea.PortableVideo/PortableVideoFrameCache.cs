using System.Buffers;
using Kinovea.PortablePixels;

namespace Kinovea.PortableVideo;

/// <summary>
/// A bounded, synchronous cache of detached video frames for the portable
/// prototype. Cache entries own their pixels and are therefore valid after the
/// decoder advances or closes.
/// </summary>
/// <remarks>
/// This is deliberately separate from the legacy Cache and PreBuffer2 classes.
/// Calls must be externally serialized. It provides bounded replacement and
/// deterministic disposal only; it does not reproduce prebuffer threading,
/// decoder back-pressure, or the legacy ForceAdd policy.
/// </remarks>
public sealed class PortableVideoFrameCache : IDisposable
{
    private readonly SortedDictionary<long, CachedVideoFrame> frames = [];
    private bool disposed;

    public PortableVideoFrameCache(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }
    public int Count => frames.Count;

    /// <summary>Copies the borrowed decoder frame before it can be invalidated.</summary>
    public void Add(PortableVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (frames.Remove(frame.Timestamp, out CachedVideoFrame? replaced))
            replaced.Dispose();
        while (frames.Count >= Capacity)
        {
            KeyValuePair<long, CachedVideoFrame> oldest = frames.First();
            frames.Remove(oldest.Key);
            oldest.Value.Dispose();
        }
        frames.Add(frame.Timestamp, CachedVideoFrame.CopyOf(frame));
    }

    public bool TryGetClosest(long timestamp, out CachedVideoFrame? frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        frame = null;
        if (frames.Count == 0) return false;
        KeyValuePair<long, CachedVideoFrame>? before = null;
        foreach (KeyValuePair<long, CachedVideoFrame> candidate in frames)
        {
            if (candidate.Key >= timestamp)
            {
                frame = before is null || candidate.Key - timestamp < timestamp - before.Value.Key
                    ? candidate.Value : before.Value.Value;
                return true;
            }
            before = candidate;
        }
        frame = before!.Value.Value;
        return true;
    }

    public void Clear()
    {
        foreach (CachedVideoFrame frame in frames.Values) frame.Dispose();
        frames.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        Clear();
        disposed = true;
    }
}

/// <summary>An independently owned cached frame. Dispose releases its pixels.</summary>
public sealed class CachedVideoFrame : IDisposable
{
    private readonly PixelBuffer pixels;
    private bool disposed;

    private CachedVideoFrame(PixelBuffer pixels, long timestamp, long previousTimestamp)
    {
        this.pixels = pixels;
        Timestamp = timestamp;
        PreviousTimestamp = previousTimestamp;
    }

    public long Timestamp { get; }
    public long PreviousTimestamp { get; }
    public int Width => pixels.Width;
    public int Height => pixels.Height;
    public ReadOnlySpan<byte> GetRowSpan(int y) => pixels.GetRowSpan(y);

    internal static CachedVideoFrame CopyOf(PortableVideoFrame source)
    {
        int rowBytes = checked(source.Width * 3);
        int length = checked(rowBytes * source.Height);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        try
        {
            Span<byte> destination = owner.Memory.Span;
            for (int y = 0; y < source.Height; y++)
                source.GetRowSpan(y).CopyTo(destination.Slice(y * rowBytes, rowBytes));
            PixelBuffer pixels = PixelBuffer.Create(source.Width, source.Height, PixelFormat.Rgb24, AlphaMode.None, rowBytes, 0, owner);
            owner = null!;
            return new CachedVideoFrame(pixels, source.Timestamp, source.PreviousTimestamp);
        }
        finally { owner?.Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pixels.Dispose();
    }
}
