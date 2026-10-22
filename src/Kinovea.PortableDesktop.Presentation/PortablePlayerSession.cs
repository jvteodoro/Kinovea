using Avalonia.Media.Imaging;
using Kinovea.PortableReaderShell;
using PortableReader = Kinovea.PortableReaderShell.PortableReaderShell;

namespace Kinovea.PortableDesktop.Presentation;

public enum PortablePlayerStatus { Empty, Open, Failed }

/// <summary>Owns one synchronous reader session and its independent presentation bitmap.</summary>
public sealed class PortablePlayerSession : IDisposable
{
    private PortableReader? reader;
    private WriteableBitmap? presentationBitmap;
    private bool disposed;

    public event EventHandler? Changed;
    public PortablePlayerStatus Status { get; private set; } = PortablePlayerStatus.Empty;
    public PortableReaderMetadata? Metadata => reader?.Metadata;
    public PortableReaderFrame Current => reader?.Current ?? PortableReaderFrame.Empty;
    public WriteableBitmap? PresentationBitmap => presentationBitmap;
    public string? CurrentPath { get; private set; }
    public string? ErrorMessage { get; private set; }
    public long PresentationCopies { get; private set; }
    public long PresentationBytesCopied { get; private set; }
    public long TimelineStart => reader?.WorkingZoneStart ?? 0;
    public long TimelineEnd => reader?.WorkingZoneEnd ?? 0;

    public bool CanStepBackward => reader is { IsLoaded: true } && Current.Image is not null && Current.Timestamp > reader.WorkingZoneStart;
    public bool CanStepForward => reader is { IsLoaded: true } && Current.Image is not null && Current.Timestamp < reader.WorkingZoneEnd;

    public bool Open(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var candidate = new PortableReader();
        PortableOpenResult result;
        try
        {
            result = candidate.Open(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            candidate.Dispose();
            return RecordOpenFailure(path, $"Could not open '{Path.GetFileName(path)}' ({exception.GetType().Name}).");
        }

        if (result != PortableOpenResult.Success || !candidate.MoveRequest(next: true, target: -1) || candidate.Current.Image is null)
        {
            candidate.Dispose();
            return RecordOpenFailure(path, $"Could not open '{Path.GetFileName(path)}' as a supported BMP or PNG image.");
        }

        PixelBitmapResult converted;
        try
        {
            converted = PixelBufferBitmapAdapter.Create(candidate.Current.Image);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            candidate.Dispose();
            return RecordOpenFailure(path, $"Could not present '{Path.GetFileName(path)}' ({exception.GetType().Name}).");
        }

        PortableReader? previousReader = reader;
        WriteableBitmap? previousBitmap = presentationBitmap;
        reader = candidate;
        presentationBitmap = converted.Bitmap;
        PresentationCopies++;
        PresentationBytesCopied += converted.BytesCopied;
        Status = PortablePlayerStatus.Open;
        CurrentPath = path;
        ErrorMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);
        previousReader?.Close();
        previousReader?.Dispose();
        previousBitmap?.Dispose();
        return true;
    }

    public bool RepeatCurrent()
    {
        if (reader is null || Current.Image is null) return false;
        var prior = Current;
        reader.PlayerRequest(PortablePlayerAction.Timestamp, prior.Timestamp);
        Changed?.Invoke(this, EventArgs.Empty);
        return ReferenceEquals(prior, Current);
    }

    public bool StepForward() => Step(PortablePlayerAction.StepForward, CanStepForward);
    public bool StepBackward() => Step(PortablePlayerAction.StepBackward, CanStepBackward);

    public bool Seek(long timestamp)
    {
        if (reader is null || timestamp < reader.WorkingZoneStart || timestamp > reader.WorkingZoneEnd) return false;
        reader.PlayerRequest(PortablePlayerAction.Timestamp, timestamp);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Close()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CloseCurrent();
        Status = PortablePlayerStatus.Empty;
        CurrentPath = null;
        ErrorMessage = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        CloseCurrent();
        disposed = true;
    }

    private bool Step(PortablePlayerAction action, bool permitted)
    {
        if (reader is null || !permitted) return false;
        reader.PlayerRequest(action, Current.Timestamp);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void CloseCurrent()
    {
        WriteableBitmap? previousBitmap = presentationBitmap;
        presentationBitmap = null;
        reader?.Close();
        reader?.Dispose();
        reader = null;
        Status = PortablePlayerStatus.Empty;
        CurrentPath = null;
        ErrorMessage = null;
        if (previousBitmap is not null)
            Changed?.Invoke(this, EventArgs.Empty);
        previousBitmap?.Dispose();
    }

    private bool RecordOpenFailure(string path, string message)
    {
        bool keepCurrent = reader is { IsLoaded: true } && presentationBitmap is not null;
        Status = keepCurrent ? PortablePlayerStatus.Open : PortablePlayerStatus.Failed;
        if (!keepCurrent)
            CurrentPath = path;
        ErrorMessage = message;
        Changed?.Invoke(this, EventArgs.Empty);
        return false;
    }
}
