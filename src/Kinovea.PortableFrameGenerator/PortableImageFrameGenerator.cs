using Kinovea.PortablePixels;
using ImageReader = Kinovea.PortableImageReader.PortableImageReader;

namespace Kinovea.PortableFrameGenerator;

/// <summary>
/// Standalone synchronous image-frame generator. It does not implement Kinovea's
/// .NET Framework IFrameGenerator and is not connected to playback or caches.
/// </summary>
/// <remarks>
/// One decoded PixelBuffer is retained for an open path and shared by every
/// frame returned during that open interval, matching the legacy image
/// generator's same-bitmap reuse. The generator alone owns that buffer.
/// Calls must be externally serialized; concurrent Generate, Close, or
/// Dispose and asynchronous retention are unsupported.
/// </remarks>
public sealed class PortableImageFrameGenerator : IPortableFrameSource
{
    private readonly ImageReader reader;
    private PixelBuffer? pixels;
    private bool disposed;

    public PortableImageFrameGenerator() : this(new ImageReader()) { }

    internal PortableImageFrameGenerator(ImageReader reader) => this.reader = reader;

    public bool IsOpen => pixels is not null;
    public int Width => pixels?.Width ?? throw new InvalidOperationException("Open an image before reading its dimensions.");
    public int Height => pixels?.Height ?? throw new InvalidOperationException("Open an image before reading its dimensions.");

    /// <summary>Decodes a BMP/PNG and takes ownership of its pixel buffer.</summary>
    public void Open(string path)
    {
        ThrowIfDisposed();
        PixelBuffer decoded = reader.Read(path);
        PixelBuffer? previous = pixels;
        pixels = decoded;
        previous?.Dispose();
    }

    /// <summary>
    /// Returns a new timestamp wrapper over the shared decoded storage.
    /// PreviousTimestamp is timestamp minus one, as in VideoReaderBitmap's
    /// single-image reader with one timestamp per frame.
    /// </summary>
    public PortableImageFrame Generate(long timestamp)
    {
        ThrowIfDisposed();
        PixelBuffer current = pixels ?? throw new InvalidOperationException("Open an image before generating a frame.");
        return new PortableImageFrame(current, timestamp, unchecked(timestamp - 1));
    }

    /// <summary>Closes the current image and releases its owned pixels.</summary>
    public void Close()
    {
        PixelBuffer? current = pixels;
        pixels = null;
        current?.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        Close();
        disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
