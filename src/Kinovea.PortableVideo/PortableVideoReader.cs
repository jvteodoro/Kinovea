using System.Buffers;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Kinovea.PortablePixels;

namespace Kinovea.PortableVideo;

/// <summary>
/// Synchronous FFmpeg decoder for a single video stream. It emits RGB24 frames
/// in decoded presentation order and reports timestamps in the stream timebase.
/// Native FFmpeg libraries must be installed and ABI-compatible with the
/// FFmpeg.AutoGen 8.1 bindings.
/// </summary>
/// <remarks>
/// Calls must be externally serialized. The reader owns the current pixel
/// buffer; each returned frame borrows it until the next ReadNext, Seek, Close,
/// or Dispose. Consumers that need a longer lifetime must copy synchronously.
/// This source does not implement Kinovea's caches, rotation, frame skipping,
/// hardware decoding, or legacy timestamp conversion.
/// </remarks>
public sealed unsafe class PortableVideoReader : IDisposable
{
    private AVFormatContext* format;
    private AVCodecContext* codecContext;
    private AVPacket* packet;
    private AVFrame* decodedFrame;
    private SwsContext* scaler;
    private int streamIndex = -1;
    private PixelBuffer? currentPixels;
    private long previousTimestamp = ffmpeg.AV_NOPTS_VALUE;
    private long? seekTarget;
    private bool inputEnded;
    private bool flushSent;
    private bool disposed;

    public bool IsOpen => format != null;
    public PortableVideoMetadata? Metadata { get; private set; }

    /// <summary>Opens the first decodable video stream in a media file.</summary>
    public void Open(string path)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        CloseCore();

        try
        {
            PortableFfmpegRuntime.Initialize();
            ffmpeg.avformat_network_init();
            AVFormatContext* candidateFormat = null;
            Check(ffmpeg.avformat_open_input(&candidateFormat, path, null, null), "open input");
            format = candidateFormat;
            Check(ffmpeg.avformat_find_stream_info(format, null), "read stream information");

            AVCodec* decoder = null;
            streamIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
            Check(streamIndex, "find video stream");
            if (decoder == null)
                throw new InvalidDataException("FFmpeg found no decoder for the video stream.");

            AVStream* stream = format->streams[streamIndex];
            codecContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (codecContext == null)
                throw new OutOfMemoryException("FFmpeg could not allocate a decoder context.");
            Check(ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar), "copy codec parameters");
            codecContext->pkt_timebase = stream->time_base;
            Check(ffmpeg.avcodec_open2(codecContext, decoder, null), "open decoder");

            packet = ffmpeg.av_packet_alloc();
            decodedFrame = ffmpeg.av_frame_alloc();
            if (packet == null || decodedFrame == null)
                throw new OutOfMemoryException("FFmpeg could not allocate packet/frame storage.");

            AVRational timeBase = stream->time_base;
            AVRational rate = stream->avg_frame_rate;
            double averageRate = rate.den == 0 ? 0 : ffmpeg.av_q2d(rate);
            Metadata = new PortableVideoMetadata(
                codecContext->width,
                codecContext->height,
                timeBase.num,
                timeBase.den,
                averageRate,
                stream->start_time,
                stream->duration,
                Marshal.PtrToStringUTF8((nint)decoder->name) ?? "unknown");
            previousTimestamp = ffmpeg.AV_NOPTS_VALUE;
            inputEnded = false;
            flushSent = false;
        }
        catch
        {
            CloseCore();
            throw;
        }
    }

    /// <summary>Reads the next decoded presentation frame, or returns false at end of stream.</summary>
    public bool ReadNext(out PortableVideoFrame? frame)
    {
        ThrowIfDisposed();
        EnsureOpen();
        currentPixels?.Dispose();
        currentPixels = null;
        frame = null;

        while (true)
        {
            int receive = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
            if (receive == 0)
            {
                long timestamp = decodedFrame->best_effort_timestamp;
                if (timestamp == ffmpeg.AV_NOPTS_VALUE)
                    timestamp = decodedFrame->pts;
                if (seekTarget is long target && timestamp != ffmpeg.AV_NOPTS_VALUE && timestamp < target)
                {
                    previousTimestamp = timestamp;
                    ffmpeg.av_frame_unref(decodedFrame);
                    continue;
                }
                seekTarget = null;
                PixelBuffer pixels = ConvertToRgb24(decodedFrame);
                long previous = previousTimestamp;
                previousTimestamp = timestamp;
                currentPixels = pixels;
                frame = new PortableVideoFrame(pixels, timestamp, previous);
                ffmpeg.av_frame_unref(decodedFrame);
                return true;
            }

            if (receive == ffmpeg.AVERROR_EOF)
                return false;
            if (receive != AvErrorAgain)
                Check(receive, "receive decoded frame");

            if (inputEnded)
            {
                if (flushSent)
                    return false;
                Check(ffmpeg.avcodec_send_packet(codecContext, null), "flush decoder");
                flushSent = true;
                continue;
            }

            bool sent = false;
            while (!sent)
            {
                int read = ffmpeg.av_read_frame(format, packet);
                if (read == ffmpeg.AVERROR_EOF)
                {
                    inputEnded = true;
                    break;
                }
                Check(read, "read packet");
                if (packet->stream_index == streamIndex)
                {
                    Check(ffmpeg.avcodec_send_packet(codecContext, packet), "send packet");
                    sent = true;
                }
                ffmpeg.av_packet_unref(packet);
            }
        }
    }

    /// <summary>
    /// Seeks to a stream timestamp. The next read returns the first decoded
    /// frame whose best-effort presentation timestamp is at or after target.
    /// </summary>
    public void Seek(long targetTimestamp)
    {
        ThrowIfDisposed();
        EnsureOpen();
        currentPixels?.Dispose();
        currentPixels = null;
        Check(ffmpeg.av_seek_frame(format, streamIndex, targetTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD), "seek stream");
        ffmpeg.avcodec_flush_buffers(codecContext);
        ffmpeg.av_packet_unref(packet);
        ffmpeg.av_frame_unref(decodedFrame);
        seekTarget = targetTimestamp;
        inputEnded = false;
        flushSent = false;
        previousTimestamp = ffmpeg.AV_NOPTS_VALUE;
    }

    public void Close()
    {
        ThrowIfDisposed();
        CloseCore();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        CloseCore();
        disposed = true;
    }

    private PixelBuffer ConvertToRgb24(AVFrame* source)
    {
        int width = source->width;
        int height = source->height;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("FFmpeg returned an invalid frame size.");

        scaler = ffmpeg.sws_getCachedContext(
            scaler, width, height, (AVPixelFormat)source->format,
            width, height, AVPixelFormat.AV_PIX_FMT_RGB24,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (scaler == null)
            throw new InvalidOperationException("FFmpeg could not create the RGB24 conversion context.");

        int rowBytes = checked(width * 3);
        int bufferLength = checked(rowBytes * height);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Span<byte> destinationSpan = owner.Memory.Span[..bufferLength];
            fixed (byte* destinationBase = destinationSpan)
            {
                byte*[] sourceData = new byte*[4];
                int[] sourceStride = new int[4];
                byte*[] destinationData = new byte*[4];
                int[] destinationStride = new int[4];
                for (uint plane = 0; plane < 4; plane++)
                {
                    sourceData[plane] = source->data[plane];
                    sourceStride[plane] = source->linesize[plane];
                    destinationData[plane] = null;
                    destinationStride[plane] = 0;
                }
                destinationData[0] = destinationBase;
                destinationStride[0] = rowBytes;
                int rows = ffmpeg.sws_scale(scaler, sourceData, sourceStride, 0, height, destinationData, destinationStride);
                if (rows != height)
                    throw new InvalidDataException($"FFmpeg converted {rows} of {height} frame rows.");
            }

            PixelBuffer result = PixelBuffer.Create(width, height, PixelFormat.Rgb24, AlphaMode.None, rowBytes, 0, owner);
            owner = null!;
            return result;
        }
        finally
        {
            owner?.Dispose();
        }
    }

    private void CloseCore()
    {
        currentPixels?.Dispose();
        currentPixels = null;
        if (scaler != null)
        {
            ffmpeg.sws_freeContext(scaler);
            scaler = null;
        }
        if (packet != null)
        {
            AVPacket* value = packet;
            ffmpeg.av_packet_free(&value);
            packet = null;
        }
        if (decodedFrame != null)
        {
            AVFrame* value = decodedFrame;
            ffmpeg.av_frame_free(&value);
            decodedFrame = null;
        }
        if (codecContext != null)
        {
            AVCodecContext* value = codecContext;
            ffmpeg.avcodec_free_context(&value);
            codecContext = null;
        }
        if (format != null)
        {
            AVFormatContext* value = format;
            ffmpeg.avformat_close_input(&value);
            format = null;
        }
        streamIndex = -1;
        Metadata = null;
        previousTimestamp = ffmpeg.AV_NOPTS_VALUE;
        seekTarget = null;
        inputEnded = false;
        flushSent = false;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("Open a video before reading frames.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static void Check(int result, string operation)
    {
        if (result >= 0)
            return;
        byte* message = stackalloc byte[1024];
        ffmpeg.av_strerror(result, message, 1024);
        string detail = Marshal.PtrToStringUTF8((nint)message) ?? $"FFmpeg error {result}";
        throw new InvalidDataException($"FFmpeg could not {operation}: {detail} ({result}).");
    }

    // AVERROR(EAGAIN) is -11 on the supported FFmpeg 8.1 Linux/Windows C ABIs.
    private const int AvErrorAgain = -11;
}
