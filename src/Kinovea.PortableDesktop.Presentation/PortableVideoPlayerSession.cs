using Avalonia.Media.Imaging;
using Kinovea.PortableVideo;

namespace Kinovea.PortableDesktop.Presentation;

/// <summary>Owns a portable video decoder and detached Avalonia presentation images.</summary>
/// <remarks>
/// FFmpeg decode runs on a worker. PixelBuffer data is copied to a separate
/// Avalonia bitmap before the decoder advances. Session operations are
/// serialized; callers should await them and publish Changed events on the UI
/// dispatcher. No cache or legacy reader is involved.
/// </remarks>
public sealed class PortableVideoPlayerSession : IAsyncDisposable
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private PortableVideoReader? reader;
    private WriteableBitmap? presentationBitmap;
    private WriteableBitmap? pendingPresentation;
    private readonly List<long> knownTimestamps = [];
    private PortablePlaybackClock? playbackClock;
    private long currentTimestamp;
    private long? pendingTimestamp;
    private bool disposed;

    public event EventHandler? Changed;
    public PortablePlayerStatus Status { get; private set; } = PortablePlayerStatus.Empty;
    public PortableVideoMetadata? Metadata => reader?.Metadata;
    public WriteableBitmap? PresentationBitmap => presentationBitmap;
    public string? CurrentPath { get; private set; }
    public string? ErrorMessage { get; private set; }
    public long CurrentTimestamp => currentTimestamp;
    public long TimelineStart => Metadata?.StartTimestamp is long start && start != long.MinValue ? start : 0;
    public long TimelineEnd => Metadata is { } metadata && metadata.DurationTimestamp >= 0
        ? SaturatingAdd(TimelineStart, metadata.DurationTimestamp)
        : currentTimestamp;
    public bool CanStepBackward => reader is not null && knownTimestamps.Any(timestamp => timestamp < currentTimestamp);
    public bool CanStepForward => reader is not null && Status == PortablePlayerStatus.Open;
    public bool IsPlaying => playbackClock?.IsPlaying == true;
    public double PlaybackRate => playbackClock?.SpeedFactor ?? 1.0;
    public long PresentationCopies { get; private set; }
    public long PresentationBytesCopied { get; private set; }

    public Task<bool> OpenAsync(string path) => OpenAsync(path, null);

    /// <summary>Opens a video and optionally presents an exact stream PTS as its first frame.</summary>
    public async Task<bool> OpenAsync(string path, long? initialTimestamp)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await operationGate.WaitAsync();
        try
        {
            PortableVideoReader candidate = new();
            PortableVideoFrame? first = null;
            try
            {
                await Task.Run(() =>
                {
                    candidate.Open(path);
                    if (initialTimestamp is long target)
                        candidate.Seek(target);
                    if (!candidate.ReadNext(out first) || first is null)
                        throw new InvalidDataException("The video contains no decodable frame.");
                    if (initialTimestamp is long expected && first.Timestamp != expected)
                        throw new InvalidDataException($"The video has no frame at the requested timestamp {expected}.");
                });
                PortableVideoFrame firstFrame = first ?? throw new InvalidDataException("The video contains no decodable frame.");
                PixelBitmapResult converted = PixelBufferBitmapAdapter.Create(firstFrame);
                PortableVideoReader? previousReader = reader;
                WriteableBitmap? previousBitmap = presentationBitmap;
                WriteableBitmap? previousPending = pendingPresentation;
                reader = candidate;
                presentationBitmap = converted.Bitmap;
                pendingPresentation = null;
                pendingTimestamp = null;
                currentTimestamp = firstFrame.Timestamp;
                knownTimestamps.Clear();
                knownTimestamps.Add(currentTimestamp);
                PortableVideoMetadata metadata = candidate.Metadata!;
                playbackClock = new PortablePlaybackClock(metadata.TimeBaseNumerator, metadata.TimeBaseDenominator);
                CurrentPath = path;
                Status = PortablePlayerStatus.Open;
                ErrorMessage = null;
                RecordPresentation(converted);
                Changed?.Invoke(this, EventArgs.Empty);
                previousReader?.Dispose();
                previousBitmap?.Dispose();
                previousPending?.Dispose();
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                candidate.Dispose();
                ErrorMessage = $"Could not open video '{Path.GetFileName(path)}' ({exception.GetType().Name}).";
                Status = reader is null ? PortablePlayerStatus.Failed : PortablePlayerStatus.Open;
                Changed?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<bool> StepForwardAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationGate.WaitAsync();
        try
        {
            if (reader is null) return false;
            if (pendingPresentation is not null && pendingTimestamp is long pending)
            {
                PublishPending(pending);
                return true;
            }
            return await ReadAndPresentNextAsync();
        }
        finally { operationGate.Release(); }
    }

    public async Task<bool> StepBackwardAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationGate.WaitAsync();
        try
        {
            if (reader is null) return false;
            long? target = knownTimestamps.Where(timestamp => timestamp < currentTimestamp).DefaultIfEmpty(long.MinValue).Max();
            if (target == long.MinValue) return false;
            ClearPending();
            PortableVideoFrame? frame = null;
            await Task.Run(() =>
            {
                reader.Seek(target.Value);
                if (!reader.ReadNext(out frame) || frame is null)
                    throw new InvalidDataException("Could not decode the previous frame after seeking.");
            });
            Present(frame!);
            return true;
        }
        finally { operationGate.Release(); }
    }

    public async Task<bool> SeekAsync(long timestamp)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationGate.WaitAsync();
        try
        {
            if (reader is null) return false;
            ClearPending();
            PortableVideoFrame? frame = null;
            await Task.Run(() =>
            {
                reader.Seek(timestamp);
                if (!reader.ReadNext(out frame) || frame is null)
                    throw new InvalidDataException("No video frame exists at or after the requested timestamp.");
            });
            Present(frame!);
            if (playbackClock?.IsPlaying == true)
                playbackClock.Play(currentTimestamp);
            return true;
        }
        finally { operationGate.Release(); }
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (reader is null || playbackClock is null) return;
        playbackClock.Play(currentTimestamp);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        playbackClock?.Pause();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetPlaybackRate(double rate)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (playbackClock is null)
            return;
        playbackClock.SetSpeedFactor(rate);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Call periodically from the UI timer; decode work is asynchronous.</summary>
    public async Task<bool> PlaybackTickAsync(bool loopAtEnd = true)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (playbackClock is not { IsPlaying: true }) return false;
        await operationGate.WaitAsync();
        try
        {
            if (reader is null || playbackClock is null || !playbackClock.IsPlaying) return false;
            if (pendingPresentation is null)
            {
                PortableVideoFrame? next = null;
                bool read = await Task.Run(() => reader.ReadNext(out next) && next is not null);
                if (!read)
                {
                    if (loopAtEnd)
                    {
                        PortableVideoFrame? first = null;
                        await Task.Run(() =>
                        {
                            reader.Seek(TimelineStart);
                            if (!reader.ReadNext(out first) || first is null)
                                throw new InvalidDataException("Could not decode the first frame after looping.");
                        });
                        Present(first!);
                        playbackClock.Play(currentTimestamp);
                        return true;
                    }
                    playbackClock.Pause();
                    Changed?.Invoke(this, EventArgs.Empty);
                    return false;
                }
                PixelBitmapResult converted = PixelBufferBitmapAdapter.Create(next!);
                pendingPresentation = converted.Bitmap;
                pendingTimestamp = next!.Timestamp;
                RecordPresentation(converted);
            }

            if (pendingTimestamp is long due && playbackClock.TargetTimestamp >= due)
            {
                PublishPending(due);
                return true;
            }
            return false;
        }
        finally { operationGate.Release(); }
    }

    public async Task CloseAsync()
    {
        if (disposed) return;
        await operationGate.WaitAsync();
        try
        {
            playbackClock?.Stop();
            reader?.Dispose();
            reader = null;
            presentationBitmap?.Dispose();
            presentationBitmap = null;
            ClearPending();
            knownTimestamps.Clear();
            CurrentPath = null;
            ErrorMessage = null;
            Status = PortablePlayerStatus.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { operationGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await CloseAsync();
        disposed = true;
        operationGate.Dispose();
    }

    private async Task<bool> ReadAndPresentNextAsync()
    {
        PortableVideoFrame? frame = null;
        bool read = await Task.Run(() => reader!.ReadNext(out frame) && frame is not null);
        if (!read) return false;
        Present(frame!);
        return true;
    }

    private void Present(PortableVideoFrame frame)
    {
        PixelBitmapResult converted = PixelBufferBitmapAdapter.Create(frame);
        presentationBitmap?.Dispose();
        presentationBitmap = converted.Bitmap;
        currentTimestamp = frame.Timestamp;
        AddKnownTimestamp(currentTimestamp);
        RecordPresentation(converted);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddKnownTimestamp(long timestamp)
    {
        if (!knownTimestamps.Contains(timestamp))
        {
            knownTimestamps.Add(timestamp);
            knownTimestamps.Sort();
        }
    }

    private void ClearPending()
    {
        pendingPresentation?.Dispose();
        pendingPresentation = null;
        pendingTimestamp = null;
    }

    private void PublishPending(long timestamp)
    {
        presentationBitmap?.Dispose();
        presentationBitmap = pendingPresentation;
        pendingPresentation = null;
        pendingTimestamp = null;
        currentTimestamp = timestamp;
        AddKnownTimestamp(currentTimestamp);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RecordPresentation(PixelBitmapResult converted)
    {
        PresentationCopies += converted.CopyCount;
        PresentationBytesCopied += converted.BytesCopied;
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
}
