using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FFmpeg.AutoGen;

internal static unsafe class Program
{
    private const int ErrorAgain = -11;
    private const int ErrorEof = -541478725;

    private static int Main(string[] args)
    {
        try
        {
            Arguments options = Arguments.Parse(args);
            ffmpeg.RootPath = options.LibraryDirectory;
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            ProbeReport report = Decode(options.InputPath);
            report.RepeatedOpenCount = 1;
            report.RepeatedRunsConsistent = true;
            double elapsedMilliseconds = report.ElapsedMilliseconds;
            long outputPixelBytes = report.Frames.Sum(frame => Convert.FromBase64String(frame.RgbaBase64).LongLength);
            for (int run = 1; run < options.RepeatCount; run++)
            {
                ProbeReport repeated = Decode(options.InputPath);
                report.RepeatedRunsConsistent &= SameObservations(report.Frames, repeated.Frames);
                report.RepeatedOpenCount++;
                elapsedMilliseconds += repeated.ElapsedMilliseconds;
                outputPixelBytes += repeated.Frames.Sum(frame => Convert.FromBase64String(frame.RgbaBase64).LongLength);
            }
            report.ElapsedMilliseconds = elapsedMilliseconds;
            report.OutputPixelBytes = outputPixelBytes;
            report.FullFrameCopyCount = checked(report.Frames.Count * options.RepeatCount);
            report.ManagedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            report.InputPath = Path.GetFullPath(options.InputPath);
            report.InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.InputPath))).ToLowerInvariant();
            report.Environment = new ProbeEnvironment(
                Environment.OSVersion.ToString(), RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.Version.ToString(),
                NativeVersion("avutil", ffmpeg.avutil_version()),
                NativeVersion("avcodec", ffmpeg.avcodec_version()),
                NativeVersion("avformat", ffmpeg.avformat_version()),
                NativeVersion("swscale", ffmpeg.swscale_version()),
                ffmpeg.av_version_info() ?? "unknown",
                options.LibraryDirectory);

            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (options.OutputPath is null)
                Console.WriteLine(json);
            else
                File.WriteAllText(options.OutputPath, json + Environment.NewLine);

            Console.Error.WriteLine($"Decoded {report.Frames.Count} frame(s) in {report.RepeatedOpenCount} open/close interval(s), {report.Width}x{report.Height}, time base {report.TimeBaseNumerator}/{report.TimeBaseDenominator}.");
            return report.Frames.Count == 0 || !report.RepeatedRunsConsistent ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static ProbeReport Decode(string inputPath)
    {
        AVFormatContext* format = null;
        AVCodecContext* codecContext = null;
        AVPacket* packet = null;
        AVFrame* frame = null;
        SwsContext* scaler = null;
        var frames = new List<FrameObservation>();
        int videoStreamIndex = -1;
        AVRational streamTimeBase = default;
        int outputWidth = 0;
        int outputHeight = 0;
        long allocatedBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            int result = ffmpeg.avformat_open_input(&format, inputPath, null, null);
            Check(result, "avformat_open_input");
            Check(ffmpeg.avformat_find_stream_info(format, null), "avformat_find_stream_info");

            AVCodec* decoder = null;
            videoStreamIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
            Check(videoStreamIndex, "av_find_best_stream");
            AVStream* stream = format->streams[videoStreamIndex];
            streamTimeBase = stream->time_base;
            outputWidth = stream->codecpar->width;
            outputHeight = stream->codecpar->height;

            codecContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (codecContext == null)
                throw new InvalidOperationException("avcodec_alloc_context3 returned null.");
            Check(ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar), "avcodec_parameters_to_context");
            codecContext->pkt_timebase = streamTimeBase;
            Check(ffmpeg.avcodec_open2(codecContext, decoder, null), "avcodec_open2");

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            if (packet == null || frame == null)
                throw new InvalidOperationException("Could not allocate AVPacket or AVFrame.");

            while ((result = ffmpeg.av_read_frame(format, packet)) >= 0)
            {
                if (packet->stream_index == videoStreamIndex)
                {
                    Check(ffmpeg.avcodec_send_packet(codecContext, packet), "avcodec_send_packet");
                    ReceiveFrames(codecContext, frame, &scaler, frames, ref outputWidth, ref outputHeight, ref allocatedBytes);
                }
                ffmpeg.av_packet_unref(packet);
            }

            if (result != ErrorEof)
                Check(result, "av_read_frame");

            Check(ffmpeg.avcodec_send_packet(codecContext, null), "avcodec_send_packet(flush)");
            ReceiveFrames(codecContext, frame, &scaler, frames, ref outputWidth, ref outputHeight, ref allocatedBytes);
            stopwatch.Stop();
            return new ProbeReport
            {
                SchemaVersion = 1,
                Width = outputWidth,
                Height = outputHeight,
                TimeBaseNumerator = streamTimeBase.num,
                TimeBaseDenominator = streamTimeBase.den,
                FrameCount = frames.Count,
                Frames = frames,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                ManagedAllocatedBytes = allocatedBytes,
                FullFrameCopyCount = frames.Count,
                OutputPixelFormat = "RGBA32",
                OutputAlphaMode = "Opaque",
                Status = "PASS"
            };
        }
        finally
        {
            if (scaler != null) ffmpeg.sws_freeContext(scaler);
            if (frame != null) ffmpeg.av_frame_free(&frame);
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (codecContext != null) ffmpeg.avcodec_free_context(&codecContext);
            if (format != null) ffmpeg.avformat_close_input(&format);
        }
    }

    private static bool SameObservations(IReadOnlyList<FrameObservation> left, IReadOnlyList<FrameObservation> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private static void ReceiveFrames(AVCodecContext* codec, AVFrame* frame, SwsContext** scaler,
        List<FrameObservation> frames, ref int width, ref int height, ref long allocatedBytes)
    {
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(codec, frame);
            if (result == ErrorAgain || result == ErrorEof)
                return;
            Check(result, "avcodec_receive_frame");

            width = frame->width;
            height = frame->height;
            if (*scaler == null)
            {
                *scaler = ffmpeg.sws_getContext(width, height, (AVPixelFormat)frame->format,
                    width, height, AVPixelFormat.AV_PIX_FMT_RGBA, (int)SwsFlags.SWS_POINT, null, null, null);
                if (*scaler == null)
                    throw new InvalidOperationException("sws_getContext returned null.");
            }

            int rowBytes = checked(width * 4);
            byte[] rgba = new byte[checked(rowBytes * height)];
            allocatedBytes += rgba.Length;
            int scaledRows;
            byte*[] destination = new byte*[4];
            int[] destinationStride = new int[4];
            fixed (byte* target = rgba)
            {
                destination[0] = target;
                destination[1] = null;
                destination[2] = null;
                destination[3] = null;
                destinationStride[0] = rowBytes;
                destinationStride[1] = 0;
                destinationStride[2] = 0;
                destinationStride[3] = 0;
                scaledRows = ffmpeg.sws_scale(*scaler, frame->data, frame->linesize, 0, height, destination, destinationStride);
            }
            if (scaledRows != height)
                throw new InvalidOperationException($"sws_scale produced {scaledRows} rows; expected {height}.");

            frames.Add(new FrameObservation(
                frames.Count, frame->best_effort_timestamp, frame->format, frame->linesize[0],
                ffmpeg.av_get_pix_fmt_name((AVPixelFormat)frame->format) ?? "unknown",
                Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant(),
                Convert.ToBase64String(rgba),
                rgba[0], rgba[1], rgba[2], rgba[3]));
            ffmpeg.av_frame_unref(frame);
        }
    }

    private static string NativeVersion(string name, uint version) =>
        $"{name} {version >> 16}.{(version >> 8) & 0xff}.{version & 0xff} (raw {version})";

    private static void Check(int result, string operation)
    {
        if (result >= 0)
            return;
        byte* buffer = stackalloc byte[256];
        ffmpeg.av_strerror(result, buffer, 256);
        string message = Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"FFmpeg error {result}";
        throw new InvalidOperationException($"{operation} failed: {message} ({result}).");
    }

    private sealed record Arguments(string InputPath, string? OutputPath, string LibraryDirectory, int RepeatCount)
    {
        public static Arguments Parse(string[] args)
        {
            string? input = null, output = null;
            string libraries = Environment.GetEnvironmentVariable("FFMPEG_LIBRARY_DIR") ?? "/usr/lib64";
            int repeatCount = 1;
            for (int i = 0; i < args.Length; i++)
            {
                string value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
                switch (args[i - 1])
                {
                    case "--input": input = value; break;
                    case "--output": output = value; break;
                    case "--library-dir": libraries = value; break;
                    case "--repeat-count":
                        if (!int.TryParse(value, out repeatCount) || repeatCount < 1)
                            throw new ArgumentException("--repeat-count must be a positive integer.");
                        break;
                    default: throw new ArgumentException($"Unknown option {args[i - 1]}.");
                }
            }
            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
                throw new ArgumentException("Usage: FFmpegDecoderProbe --input <video> [--output <json>] [--library-dir <dir>].");
            if (!Directory.Exists(libraries))
                throw new DirectoryNotFoundException($"FFmpeg library directory not found: {libraries}");
            return new Arguments(input, output, libraries, repeatCount);
        }
    }

    private sealed class ProbeReport
    {
        public int SchemaVersion { get; init; }
        public string Status { get; init; } = "FAIL";
        public string InputPath { get; set; } = "";
        public string InputSha256 { get; set; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public int TimeBaseNumerator { get; init; }
        public int TimeBaseDenominator { get; init; }
        public int FrameCount { get; init; }
        public int RepeatedOpenCount { get; set; }
        public bool RepeatedRunsConsistent { get; set; }
        public string OutputPixelFormat { get; init; } = "";
        public string OutputAlphaMode { get; init; } = "";
        public long ManagedAllocatedBytes { get; set; }
        public long OutputPixelBytes { get; set; }
        public int FullFrameCopyCount { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public ProbeEnvironment? Environment { get; set; }
        public List<FrameObservation> Frames { get; init; } = [];
    }

    private sealed record FrameObservation(int Index, long Timestamp, int SourcePixelFormatValue,
        int SourceLineSize0, string SourcePixelFormat, string RgbaSha256, string RgbaBase64,
        byte Red00, byte Green00, byte Blue00, byte Alpha00);

    private sealed record ProbeEnvironment(string Os, string OsArchitecture, string ProcessArchitecture,
        string DotNetRuntime, string AvUtilVersion, string AvCodecVersion, string AvFormatVersion,
        string SwScaleVersion, string FfmpegVersion, string LibraryDirectory);
}
