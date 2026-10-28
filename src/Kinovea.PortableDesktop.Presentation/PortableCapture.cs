using System.Buffers;
using System.Diagnostics;
using Kinovea.PortablePixels;

namespace Kinovea.PortableDesktop.Presentation;

/// <summary>A captured frame that owns its portable pixel storage.</summary>
public sealed class PortableCaptureFrame : IDisposable
{
    private PixelBuffer? pixels;

    public PortableCaptureFrame(long sequence, long timestamp, PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Sequence = sequence;
        Timestamp = timestamp;
        this.pixels = pixels;
    }

    public long Sequence { get; }
    public long Timestamp { get; }
    public int Width => Pixels.Width;
    public int Height => Pixels.Height;
    public PixelFormat Format => Pixels.Format;
    public AlphaMode AlphaMode => Pixels.AlphaMode;
    public ReadOnlySpan<byte> GetRowSpan(int y) => Pixels.GetRowSpan(y);

    public void Dispose()
    {
        pixels?.Dispose();
        pixels = null;
    }

    private PixelBuffer Pixels => pixels ?? throw new ObjectDisposedException(nameof(PortableCaptureFrame));
}

/// <summary>Device-neutral asynchronous source lifecycle used by the desktop capture views.</summary>
public interface IPortableCaptureSource : IAsyncDisposable
{
    string DisplayName { get; }
    bool IsRunning { get; }
    ValueTask StartAsync(CancellationToken cancellationToken = default);
    ValueTask<PortableCaptureFrame> ReadFrameAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates capture sources without exposing or hardcoding device APIs in the UI.</summary>
public interface IPortableCaptureSourceProvider
{
    IReadOnlyList<string> SourceNames { get; }
    IPortableCaptureSource Create(string sourceName, TimeSpan frameInterval);
}

/// <summary>Deterministic demo/test source; it does not represent a physical camera.</summary>
public sealed class SyntheticCaptureSourceProvider : IPortableCaptureSourceProvider
{
    private static readonly string[] Names = ["Synthetic source A", "Synthetic source B"];
    public IReadOnlyList<string> SourceNames => Names;

    public IPortableCaptureSource Create(string sourceName, TimeSpan frameInterval)
    {
        if (!Names.Contains(sourceName, StringComparer.Ordinal))
            throw new ArgumentException("The requested synthetic source is unavailable.", nameof(sourceName));
        if (frameInterval < TimeSpan.FromMilliseconds(10) || frameInterval > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(frameInterval));
        return new SyntheticCaptureSource(sourceName, frameInterval);
    }
}

internal sealed class SyntheticCaptureSource : IPortableCaptureSource
{
    private readonly string displayName;
    private readonly TimeSpan frameInterval;
    private readonly Stopwatch clock = new();
    private CancellationTokenSource? runCancellation;
    private bool disposed;
    private long sequence;

    public SyntheticCaptureSource(string displayName, TimeSpan frameInterval)
    {
        this.displayName = displayName;
        this.frameInterval = frameInterval;
    }

    public string DisplayName => displayName;
    public bool IsRunning { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRunning)
        {
            runCancellation?.Dispose();
            runCancellation = new CancellationTokenSource();
            clock.Restart();
            IsRunning = true;
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask<PortableCaptureFrame> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsRunning)
            throw new InvalidOperationException("Capture source must be running before reading a frame.");
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runCancellation!.Token);
        await Task.Delay(frameInterval, linked.Token).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsRunning)
            throw new OperationCanceledException("Capture source stopped while waiting for a frame.", linked.Token);
        long currentSequence = sequence++;
        PixelBuffer pixels = CreatePixels(currentSequence, displayName == "Synthetic source B" ? 73 : 0);
        return new PortableCaptureFrame(currentSequence, clock.ElapsedTicks, pixels);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = false;
        runCancellation?.Cancel();
        clock.Stop();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync().ConfigureAwait(false);
        disposed = true;
        runCancellation?.Dispose();
        runCancellation = null;
        clock.Reset();
    }

    private static PixelBuffer CreatePixels(long sequence, int sourceOffset)
    {
        const int width = 320;
        const int height = 180;
        const int stride = width * 3;
        var owner = new CaptureMemoryOwner(stride * height);
        Span<byte> bytes = owner.Memory.Span;
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = bytes.Slice(y * stride, stride);
            for (int x = 0; x < width; x++)
            {
                int offset = x * 3;
                row[offset] = unchecked((byte)(x + sequence * 3 + sourceOffset));
                row[offset + 1] = unchecked((byte)(y * 2 + sequence + sourceOffset));
                row[offset + 2] = unchecked((byte)(64 + sequence * 5 + sourceOffset));
            }
        }
        return PixelBuffer.Create(width, height, PixelFormat.Rgb24, AlphaMode.None, stride, 0, owner);
    }
}

internal sealed class CaptureMemoryOwner(int length) : IMemoryOwner<byte>
{
    private byte[]? buffer = new byte[length];
    public Memory<byte> Memory => buffer ?? throw new ObjectDisposedException(nameof(CaptureMemoryOwner));
    public void Dispose() => buffer = null;
}
