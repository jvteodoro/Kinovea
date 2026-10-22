using FFmpeg.AutoGen;

namespace Kinovea.PortableVideo;

/// <summary>Version numbers reported by the native FFmpeg libraries actually loaded by this process.</summary>
public sealed record PortableFfmpegVersions(string AvFormat, string AvCodec, string AvUtil, string SwScale);

/// <summary>Configures and verifies FFmpeg 8.1 compatible shared libraries.</summary>
public static class PortableFfmpegRuntime
{
    private static readonly object Gate = new();
    private static string? initializedRoot;
    private static PortableFfmpegVersions? initializedVersions;

    /// <summary>
    /// Sets the root used by FFmpeg.AutoGen before its first native call.
    /// If omitted, KINOVEA_FFMPEG_ROOT is used, then the platform loader path.
    /// </summary>
    public static PortableFfmpegVersions Initialize(string? libraryRoot = null)
    {
        lock (Gate)
        {
            string? configured = libraryRoot ?? Environment.GetEnvironmentVariable("KINOVEA_FFMPEG_ROOT");
            if (string.IsNullOrWhiteSpace(configured) && OperatingSystem.IsLinux() && Directory.Exists("/usr/lib64"))
                configured = "/usr/lib64";
            string? root = string.IsNullOrWhiteSpace(configured) ? null : Path.GetFullPath(configured);
            if (initializedVersions is not null)
            {
                if (!StringComparer.Ordinal.Equals(root, initializedRoot))
                    throw new InvalidOperationException("The FFmpeg native library root cannot change after bindings initialization.");
                return initializedVersions;
            }

            if (root is not null)
                ffmpeg.RootPath = root;
            DynamicallyLoadedBindings.Initialize();
            uint format = ffmpeg.avformat_version();
            uint codec = ffmpeg.avcodec_version();
            uint util = ffmpeg.avutil_version();
            uint scale = ffmpeg.swscale_version();
            RequireMajor("libavformat", format, 62);
            RequireMajor("libavcodec", codec, 62);
            RequireMajor("libavutil", util, 60);
            RequireMajor("libswscale", scale, 9);
            initializedRoot = root;
            initializedVersions = new PortableFfmpegVersions(Format(format), Format(codec), Format(util), Format(scale));
            return initializedVersions;
        }
    }

    private static void RequireMajor(string name, uint version, int expected)
    {
        int major = (int)(version >> 16);
        if (major != expected)
            throw new NotSupportedException($"{name} ABI {major} is loaded; FFmpeg.AutoGen 8.1 requires ABI {expected}.");
    }

    private static string Format(uint version) => $"{version >> 16}.{(version >> 8) & 0xff}.{version & 0xff}";
}
