using Kinovea.PortablePixels;

namespace Kinovea.PortableVideo;

/// <summary>Video stream metadata; timestamps are in stream time-base units.</summary>
public sealed record PortableVideoMetadata(
    int Width,
    int Height,
    int TimeBaseNumerator,
    int TimeBaseDenominator,
    double AverageFrameRate,
    long StartTimestamp,
    long DurationTimestamp,
    string CodecName);

/// <summary>
/// A borrowed decoded frame. Its pixel storage is owned by the reader and is
/// valid only until the next read, seek, close, or dispose operation.
/// </summary>
public sealed class PortableVideoFrame
{
    private readonly PixelBuffer pixels;

    internal PortableVideoFrame(PixelBuffer pixels, long timestamp, long previousTimestamp)
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
