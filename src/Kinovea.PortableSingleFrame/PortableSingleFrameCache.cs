using Kinovea.PortableReaderShell;
using ReaderShell = Kinovea.PortableReaderShell.PortableReaderShell;

namespace Kinovea.PortableSingleFrame;

/// <summary>
/// A synchronous, single-slot cache adapter over a portable reader shell.
/// The cache borrows frames; it never owns or disposes their pixel storage.
/// </summary>
/// <remarks>
/// The supplied reader shell remains the pixel owner. Clear and Dispose only
/// detach the cache's frame reference. Pixel access remains valid while the
/// reader stays open. Close clears the slot before closing the reader, which
/// releases its storage and makes previously returned frame views invalid for
/// new pixel access. Calls to this adapter and its reader must be externally
/// serialized. Do not retain a frame or borrowed memory beyond the reader's
/// open lifetime.
/// </remarks>
public sealed class PortableSingleFrameCache : IDisposable
{
    private readonly ReaderShell reader;
    private bool disposed;

    public PortableSingleFrameCache(ReaderShell reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
    }

    /// <summary>The current borrowed frame, or null when the slot is empty.</summary>
    public PortableReaderFrame? CurrentFrame { get; private set; }

    public bool IsEmpty => CurrentFrame is null;
    public PortableReaderState ReaderState => reader.State;
    public PortableReaderMetadata? Metadata => reader.Metadata;

    public PortableOpenResult Open(string path)
    {
        ThrowIfDisposed();
        Clear();
        return reader.Open(path);
    }

    public bool MoveRequest(bool next, long target)
    {
        ThrowIfDisposed();
        bool result = reader.MoveRequest(next, target);
        SynchronizeCurrent();
        return result;
    }

    public bool PlayerRequest(PortablePlayerAction action, long referenceTimestamp)
    {
        ThrowIfDisposed();
        bool result = reader.PlayerRequest(action, referenceTimestamp);
        SynchronizeCurrent();
        return result;
    }

    public void SetWorkingZone(long start, long end)
    {
        ThrowIfDisposed();
        reader.SetWorkingZone(start, end);
    }

    /// <summary>Detaches the cached reference without releasing reader-owned pixels.</summary>
    public void Clear() => CurrentFrame = null;

    /// <summary>Clears the cache slot, then closes the pixel-owning reader.</summary>
    public void Close()
    {
        if (disposed)
            return;

        Clear();
        reader.Close();
    }

    /// <summary>
    /// Detaches the cache slot. The caller still owns and must close/dispose the
    /// supplied reader shell separately.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        Clear();
        disposed = true;
    }

    private void SynchronizeCurrent()
    {
        PortableReaderFrame frame = reader.Current;
        CurrentFrame = reader.IsLoaded && frame.Image is not null ? frame : null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
