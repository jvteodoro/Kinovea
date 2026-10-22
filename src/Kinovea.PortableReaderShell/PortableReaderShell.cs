using Kinovea.PortableFrameGenerator;

namespace Kinovea.PortableReaderShell;

public enum PortableReaderState { Unopened, Open, Closed, FailedOpen, Disposed }
public enum PortableOpenResult { Success, NotSupported }
public enum PortablePlayerAction { Playback, Timestamp, StepForward, StepBackward }

public sealed record PortableReaderFrame(PortableImageFrame? Image, long Timestamp, long PreviousTimestamp)
{
    public static PortableReaderFrame Empty { get; } = new(null, -1, -1);
}

public sealed record PortableReaderMetadata(
    string FilePath, int Width, int Height,
    long FirstTimestamp, long LastTimestamp, double AverageTimestampsPerFrame,
    double FramesPerSecond, long DurationTimestamps, double FrameIntervalMilliseconds);

/// <summary>
/// Synchronous, externally serialized still-image shell reproducing
/// VideoReaderBitmap navigation. The default registry selects BMP and PNG
/// sources; alternate sources can be registered without changing the shell.
/// This shell retains the legacy still-image 25 fps navigation profile and is
/// not a variable-frame-rate video navigator. It is not connected to legacy
/// caches or UI.
/// </summary>
public sealed class PortableReaderShell : IDisposable
{
    private readonly PortableReaderRegistry registry;
    private IPortableFrameSource? source;
    private bool firstFrame = true;
    private bool disposed;
    private int currentImageGeneration;
    private long lastRequestedTimestamp;

    public PortableReaderShell() : this(PortableReaderRegistry.CreateDefault()) { }

    public PortableReaderShell(PortableReaderRegistry registry) =>
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public PortableReaderState State { get; private set; } = PortableReaderState.Unopened;
    public bool IsLoaded => State == PortableReaderState.Open;
    public PortableReaderFrame Current { get; private set; } = PortableReaderFrame.Empty;
    public PortableReaderMetadata? Metadata { get; private set; }
    public long WorkingZoneStart { get; private set; }
    public long WorkingZoneEnd { get; private set; }

    public PortableOpenResult Open(string path)
    {
        ThrowIfDisposed();
        try
        {
            IPortableFrameSource candidate = registry.CreateForPath(path);
            try
            {
                candidate.Open(path);
            }
            catch
            {
                candidate.Dispose();
                throw;
            }

            IPortableFrameSource? previous = source;
            source = candidate;
            previous?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or ArgumentException)
        {
            source?.Dispose();
            source = null;
            State = PortableReaderState.FailedOpen;
            return PortableOpenResult.NotSupported;
        }

        State = PortableReaderState.Open;
        Metadata = new PortableReaderMetadata(path, source!.Width, source.Height, 0, 0, 1, 25, 250, 40);
        WorkingZoneStart = 0;
        WorkingZoneEnd = 249;
        currentImageGeneration++;
        return PortableOpenResult.Success;
    }

    public bool MoveRequest(bool next, long target)
    {
        ThrowIfDisposed();
        if (next)
        {
            double average = Metadata?.AverageTimestampsPerFrame ?? 0;
            long timestamp = checked((long)Math.Round(Current.Timestamp + average));
            if (firstFrame) { timestamp = 0; firstFrame = false; }
            if (timestamp > WorkingZoneEnd) return false;
            UpdateCurrent(timestamp);
            return true;
        }

        UpdateCurrent(target);
        return true;
    }

    public bool PlayerRequest(PortablePlayerAction action, long referenceTimestamp)
    {
        ThrowIfDisposed();
        long target = action switch
        {
            PortablePlayerAction.Playback or PortablePlayerAction.Timestamp => referenceTimestamp,
            PortablePlayerAction.StepForward => checked((long)Math.Round(Current.Timestamp + (Metadata?.AverageTimestampsPerFrame ?? 0))),
            PortablePlayerAction.StepBackward => checked((long)Math.Round(Current.Timestamp - (Metadata?.AverageTimestampsPerFrame ?? 0))),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        UpdateCurrent(target);
        return true;
    }

    public void SetWorkingZone(long start, long end)
    {
        ThrowIfDisposed();
        WorkingZoneStart = start;
        WorkingZoneEnd = end;
    }

    public void Close()
    {
        if (disposed) return;
        source?.Dispose();
        source = null;
        State = PortableReaderState.Closed;
    }

    public void Dispose()
    {
        if (disposed) return;
        source?.Dispose();
        source = null;
        disposed = true;
        State = PortableReaderState.Disposed;
    }

    private void UpdateCurrent(long timestamp)
    {
        IPortableFrameSource? activeSource = source;
        if (activeSource is not { IsOpen: true } || timestamp < WorkingZoneStart || timestamp > WorkingZoneEnd)
            return;
        if (lastRequestedTimestamp == timestamp && Current.Image is not null && currentImageGeneration == generationAtCurrent)
            return;

        PortableImageFrame image = activeSource.Generate(timestamp);
        Current = new PortableReaderFrame(image, image.Timestamp, image.PreviousTimestamp);
        lastRequestedTimestamp = timestamp;
        generationAtCurrent = currentImageGeneration;
    }

    private int generationAtCurrent;
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
