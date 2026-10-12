using Kinovea.PortablePixels;

namespace Kinovea.PortableFrameGenerator;

/// <summary>A timestamped borrowed view of the currently open image frame.</summary>
/// <remarks>
/// The generator owns the pixel storage. This frame does not own or dispose it.
/// Its pixel access is valid only while its creating generator remains open.
/// Previously returned <see cref="ReadOnlyMemory{T}"/> values cannot be revoked;
/// callers must not retain or access them after the generator closes. This API
/// supports synchronous, externally coordinated use only.
/// </remarks>
public sealed class PortableImageFrame
{
    private readonly PixelBuffer pixels;

    internal PortableImageFrame(PixelBuffer pixels, long timestamp, long previousTimestamp)
    {
        this.pixels = pixels;
        Timestamp = timestamp;
        PreviousTimestamp = previousTimestamp;
    }

    public long Timestamp { get; }
    public long PreviousTimestamp { get; }
    public int Width => pixels.Width;
    public int Height => pixels.Height;
    public PixelFormat Format => pixels.Format;
    public AlphaMode AlphaMode => pixels.AlphaMode;
    public int StrideBytes => pixels.StrideBytes;
    public ReadOnlyMemory<byte> Memory => pixels.Memory;
    public ReadOnlySpan<byte> GetRowSpan(int y) => pixels.GetRowSpan(y);
}
