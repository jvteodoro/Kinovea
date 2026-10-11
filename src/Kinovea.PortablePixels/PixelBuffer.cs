using System.Buffers;

namespace Kinovea.PortablePixels;

/// <summary>Supported byte layouts. Component order follows the enum name.</summary>
public enum PixelFormat
{
    /// <summary>Three bytes per pixel in red, green, blue order.</summary>
    Rgb24 = 1,

    /// <summary>Four bytes per pixel in red, green, blue, alpha order.</summary>
    Rgba32 = 2
}

/// <summary>How the alpha component relates to the color components.</summary>
public enum AlphaMode
{
    None = 0,
    Straight = 1,
    Premultiplied = 2
}

/// <summary>
/// Owns a two-dimensional view over a caller-supplied memory owner. Ownership
/// transfers to this instance only when <see cref="Create"/> succeeds.
/// </summary>
/// <remarks>
/// Logical row y begins at <c>RowZeroOffset + y * StrideBytes</c>. Stride is a
/// signed byte distance between logical rows; negative values are valid when
/// RowZeroOffset points at the first logical row. Row padding is not pixel data.
/// Memory returned from <see cref="Memory"/> is borrowed and valid only while
/// this buffer is alive. Previously copied ReadOnlyMemory values cannot be
/// revoked by Dispose; callers must not use or retain them beyond this owner's
/// lifetime. The owner does not copy or normalize pixel bytes.
/// </remarks>
public sealed class PixelBuffer : IDisposable
{
    private readonly IMemoryOwner<byte> owner;
    private bool disposed;

    private PixelBuffer(
        int width,
        int height,
        PixelFormat format,
        AlphaMode alphaMode,
        int strideBytes,
        int rowZeroOffset,
        IMemoryOwner<byte> owner,
        int rowBytes)
    {
        Width = width;
        Height = height;
        Format = format;
        AlphaMode = alphaMode;
        StrideBytes = strideBytes;
        RowZeroOffset = rowZeroOffset;
        this.owner = owner;
        RowBytes = rowBytes;
    }

    public int Width { get; }
    public int Height { get; }
    public PixelFormat Format { get; }
    public AlphaMode AlphaMode { get; }

    /// <summary>Signed byte distance between adjacent logical rows.</summary>
    public int StrideBytes { get; }

    /// <summary>Storage offset of logical row zero.</summary>
    public int RowZeroOffset { get; }

    /// <summary>Number of bytes containing pixels in each logical row.</summary>
    public int RowBytes { get; }

    /// <summary>Borrowed view of owned storage. Do not use after disposal.</summary>
    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            ThrowIfDisposed();
            return owner.Memory;
        }
    }

    /// <summary>Returns a read-only view of one logical pixel row.</summary>
    public ReadOnlySpan<byte> GetRowSpan(int y)
    {
        ThrowIfDisposed();
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        int offset = checked(RowZeroOffset + checked(y * StrideBytes));
        return owner.Memory.Span.Slice(offset, RowBytes);
    }

    /// <summary>
    /// Takes ownership of <paramref name="owner"/> on success. If validation
    /// fails, the caller remains responsible for disposing it.
    /// </summary>
    public static PixelBuffer Create(
        int width,
        int height,
        PixelFormat format,
        AlphaMode alphaMode,
        int strideBytes,
        int rowZeroOffset,
        IMemoryOwner<byte> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        int bytesPerPixel = format switch
        {
            PixelFormat.Rgb24 => 3,
            PixelFormat.Rgba32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format.")
        };

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        bool hasAlpha = format == PixelFormat.Rgba32;
        if (hasAlpha && alphaMode is not (AlphaMode.Straight or AlphaMode.Premultiplied))
            throw new ArgumentException("RGBA pixels require Straight or Premultiplied alpha.", nameof(alphaMode));
        if (!hasAlpha && alphaMode != AlphaMode.None)
            throw new ArgumentException("RGB pixels do not have an alpha component.", nameof(alphaMode));

        int rowBytes;
        try
        {
            rowBytes = checked(width * bytesPerPixel);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Pixel row size overflows Int32.");
        }

        long strideMagnitude = Math.Abs((long)strideBytes);
        if (strideMagnitude < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(strideBytes), "Stride magnitude must cover a complete pixel row.");

        long lastRowOffset;
        long minimumOffset;
        long maximumOffset;
        long endOffset;
        try
        {
            lastRowOffset = checked((long)rowZeroOffset + checked((long)(height - 1) * strideBytes));
            minimumOffset = Math.Min(rowZeroOffset, lastRowOffset);
            maximumOffset = Math.Max(rowZeroOffset, lastRowOffset);
            endOffset = checked(maximumOffset + rowBytes);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(rowZeroOffset), rowZeroOffset, "Pixel row address overflows.");
        }

        if (minimumOffset < int.MinValue || endOffset > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(rowZeroOffset), rowZeroOffset, "Pixel row address cannot be represented by a Memory<byte> index.");

        int storageLength = owner.Memory.Length;
        if (minimumOffset < 0 || endOffset > storageLength)
            throw new ArgumentException("Pixel rows fall outside the supplied memory owner.", nameof(owner));

        return new PixelBuffer(width, height, format, alphaMode, strideBytes, rowZeroOffset, owner, rowBytes);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        owner.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

internal sealed class ArrayMemoryOwner : IMemoryOwner<byte>
{
    private byte[]? buffer;

    public ArrayMemoryOwner(int length)
    {
        buffer = new byte[length];
    }

    public Memory<byte> Memory => buffer ?? throw new ObjectDisposedException(nameof(ArrayMemoryOwner));

    public void Dispose()
    {
        buffer = null;
    }
}
