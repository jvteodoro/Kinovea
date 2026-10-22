using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kinovea.PortableFrameGenerator;
using Kinovea.PortablePixels;
using Kinovea.PortableVideo;

namespace Kinovea.PortableDesktop.Presentation;

/// <summary>Creates an Avalonia-owned bitmap from a borrowed portable frame.</summary>
/// <remarks>
/// Conversion is synchronous and copies each logical row exactly once. The resulting
/// Avalonia bitmap has its own storage and does not retain the frame or its owner.
/// </remarks>
public static class PixelBufferBitmapAdapter
{
    public static unsafe PixelBitmapResult Create(PortableVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format != Kinovea.PortablePixels.PixelFormat.Rgb24 || frame.AlphaMode != AlphaMode.None)
            throw new NotSupportedException($"Video presentation requires opaque RGB24; got {frame.Format}/{frame.AlphaMode}.");

        int rowBytes = checked(frame.Width * 3);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormats.Rgb24, AlphaFormat.Opaque);
        try
        {
            using ILockedFramebuffer destination = bitmap.Lock();
            if (destination.Size.Width != frame.Width || destination.Size.Height != frame.Height || destination.RowBytes < rowBytes)
                throw new InvalidOperationException("Avalonia returned an incompatible writeable framebuffer.");
            for (int y = 0; y < frame.Height; y++)
            {
                ReadOnlySpan<byte> sourceRow = frame.GetRowSpan(y);
                if (sourceRow.Length != rowBytes)
                    throw new InvalidOperationException($"Video row {y} has {sourceRow.Length} bytes; expected {rowBytes}.");
                byte* destinationRow = (byte*)destination.Address + checked(y * destination.RowBytes);
                fixed (byte* source = sourceRow)
                    Buffer.MemoryCopy(source, destinationRow, destination.RowBytes, rowBytes);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new PixelBitmapResult(bitmap, frame.Width, frame.Height, rowBytes, 1, checked((long)rowBytes * frame.Height), allocated);
    }

    public static unsafe PixelBitmapResult Create(PortableImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Avalonia.Platform.PixelFormat format = frame.Format switch
        {
            Kinovea.PortablePixels.PixelFormat.Rgb24 => PixelFormats.Rgb24,
            Kinovea.PortablePixels.PixelFormat.Rgba32 => PixelFormats.Rgba8888,
            _ => throw new NotSupportedException($"Unsupported portable format: {frame.Format}.")
        };
        AlphaFormat alpha = frame.AlphaMode switch
        {
            AlphaMode.None => AlphaFormat.Opaque,
            AlphaMode.Straight => AlphaFormat.Unpremul,
            AlphaMode.Premultiplied => AlphaFormat.Premul,
            _ => throw new NotSupportedException($"Unsupported alpha mode: {frame.AlphaMode}.")
        };
        if ((frame.Format == Kinovea.PortablePixels.PixelFormat.Rgb24) != (frame.AlphaMode == Kinovea.PortablePixels.AlphaMode.None))
            throw new ArgumentException("Portable pixel format and alpha mode do not agree.", nameof(frame));

        int bytesPerPixel = frame.Format == Kinovea.PortablePixels.PixelFormat.Rgb24 ? 3 : 4;
        int rowBytes = checked(frame.Width * bytesPerPixel);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), format, alpha);
        try
        {
            using ILockedFramebuffer destination = bitmap.Lock();
            if (destination.Size.Width != frame.Width || destination.Size.Height != frame.Height || destination.RowBytes < rowBytes)
                throw new InvalidOperationException("Avalonia returned an incompatible writeable framebuffer.");

            for (int y = 0; y < frame.Height; y++)
            {
                ReadOnlySpan<byte> sourceRow = frame.GetRowSpan(y);
                if (sourceRow.Length != rowBytes)
                    throw new InvalidOperationException($"Frame row {y} has {sourceRow.Length} bytes; expected {rowBytes}.");
                byte* destinationRow = (byte*)destination.Address + checked(y * destination.RowBytes);
                fixed (byte* source = sourceRow)
                    Buffer.MemoryCopy(source, destinationRow, destination.RowBytes, rowBytes);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new PixelBitmapResult(bitmap, frame.Width, frame.Height, rowBytes, 1, checked((long)rowBytes * frame.Height), allocated);
    }
}

public sealed record PixelBitmapResult(
    WriteableBitmap Bitmap,
    int Width,
    int Height,
    int SourceRowBytes,
    int CopyCount,
    long BytesCopied,
    long ManagedBytesAllocated);
