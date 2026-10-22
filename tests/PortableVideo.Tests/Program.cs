using System.Diagnostics;
using System.Security.Cryptography;
using Kinovea.PortableVideo;
using Kinovea.PortablePixels;

var directory = Path.Combine(Path.GetTempPath(), "kinovea-portable-video-tests");
Directory.CreateDirectory(directory);
string fixture = Path.Combine(directory, "synthetic-h264.mp4");
GenerateFixture(fixture);
string vfrFixture = Path.Combine(directory, "synthetic-vfr-h264.mp4");
GenerateVfrFixture(vfrFixture);
string nonzeroStartFixture = Path.Combine(directory, "synthetic-nonzero-start-h264.mp4");
GenerateNonzeroStartFixture(nonzeroStartFixture);
string gifFixture = Path.Combine(directory, "synthetic-animated.gif");
GenerateGifFixture(gifFixture);
string svgFixture = Path.Combine(directory, "synthetic-vector.svg");
GenerateSvgFixture(svgFixture);
PortableFfmpegVersions nativeVersions = PortableFfmpegRuntime.Initialize();

int assertions = 0;
using var reader = new PortableVideoReader();
reader.Open(fixture);
PortableVideoMetadata metadata = reader.Metadata ?? throw new Exception("Metadata missing after open.");
Equal(64, metadata.Width, "video width");
Equal(48, metadata.Height, "video height");
Equal("h264", metadata.CodecName, "codec");
True(metadata.TimeBaseNumerator > 0 && metadata.TimeBaseDenominator > 0, "stream time base present");

var clockTime = new ManualTimeProvider();
var clock = new PortablePlaybackClock(1, 4, clockTime);
clock.Play(100);
clockTime.Advance(TimeSpan.FromMilliseconds(500));
Equal(102L, clock.TargetTimestamp, "playback target uses rational timebase");
clock.Pause();
clockTime.Advance(TimeSpan.FromSeconds(1));
Equal(102L, clock.TargetTimestamp, "paused playback clock remains stopped");
clock.Resume();
clockTime.Advance(TimeSpan.FromMilliseconds(250));
Equal(103L, clock.TargetTimestamp, "resumed playback clock continues");
clock.Stop(42);
Equal(42L, clock.TargetTimestamp, "stopped clock resets to supplied timestamp");

var frames = new List<(long Timestamp, long Previous, byte R, byte G, byte B)>();
using var cache = new PortableVideoFrameCache(3);
PortableVideoFrame? firstFrame = null;
while (reader.ReadNext(out var frame))
{
    if (frame is null) throw new Exception("Reader returned success without a frame.");
    firstFrame ??= frame;
    Equal(PixelFormat.Rgb24, frame.Format, "RGB24 output format");
    Equal(AlphaMode.None, frame.AlphaMode, "no alpha output");
    Equal(frame.Width * 3, frame.StrideBytes, "packed output stride");
    ReadOnlySpan<byte> row = frame.GetRowSpan(frame.Height / 2);
    int center = (frame.Width / 2) * 3;
    frames.Add((frame.Timestamp, frame.PreviousTimestamp, row[center], row[center + 1], row[center + 2]));
    cache.Add(frame);
}
Equal(8, frames.Count, "decoded frame count including B-frame reorder");
Throws<ObjectDisposedException>(() => firstFrame!.GetRowSpan(0), "reader invalidates borrowed frame after next read");
True(frames.Zip(frames.Skip(1)).All(pair => pair.First.Timestamp < pair.Second.Timestamp), "decoded presentation timestamps increase");
Equal(long.MinValue, frames[0].Previous, "first frame has unknown previous timestamp");
for (int index = 1; index < frames.Count; index++)
    Equal(frames[index - 1].Timestamp, frames[index].Previous, "previous frame timestamp");
ColorNear(frames[0], 255, 0, 0, "first red frame");
ColorNear(frames[2], 0, 128, 0, "green frame");
ColorNear(frames[4], 0, 0, 255, "blue frame");
ColorNear(frames[6], 255, 255, 0, "yellow frame");
Equal(3, cache.Count, "bounded cache evicts oldest entries");
True(cache.TryGetClosest(frames[6].Timestamp, out var cached), "cache retrieves detached frame");
Equal(frames[6].Timestamp, cached!.Timestamp, "cache closest timestamp");
ReadOnlySpan<byte> cachedRow = cached.GetRowSpan(cached.Height / 2);
int cachedCenter = (cached.Width / 2) * 3;
Near(255, cachedRow[cachedCenter], "cached frame remains valid after decoder advanced");

reader.Seek(frames[4].Timestamp);
True(reader.ReadNext(out var sought), "seek returns a frame");
Equal(frames[4].Timestamp, sought!.Timestamp, "seek selects first frame at or after target");
ReadOnlySpan<byte> soughtRow = sought.GetRowSpan(sought.Height / 2);
int soughtCenter = (sought.Width / 2) * 3;
ColorNear((sought.Timestamp, sought.PreviousTimestamp, soughtRow[soughtCenter], soughtRow[soughtCenter + 1], soughtRow[soughtCenter + 2]), 0, 0, 255, "seeked blue frame");

reader.Seek(frames[1].Timestamp);
True(reader.ReadNext(out var backward), "backward seek returns a frame");
Equal(frames[1].Timestamp, backward!.Timestamp, "backward seek selects requested timestamp");
reader.Close();
True(!reader.IsOpen && reader.Metadata is null, "close clears state");
reader.Open(fixture);
True(reader.ReadNext(out var reopened), "reopen decodes first frame");
Equal(frames[0].Timestamp, reopened!.Timestamp, "reopened stream starts at first timestamp");
reader.Close();
reader.Close();
Near(255, cached.GetRowSpan(cached.Height / 2)[cachedCenter], "cache remains valid after decoder close");
cache.Clear();
Equal(0, cache.Count, "cache Clear releases entries");
Throws<ObjectDisposedException>(() => cached.GetRowSpan(0), "cache Clear invalidates detached entry");
reader.Dispose();

using (var cacheStressReader = new PortableVideoReader())
using (var stressCache = new PortableVideoFrameCache(3))
{
    // Synchronous bounded-cache stress; this is not a concurrency or native-cache test.
    for (int iteration = 0; iteration < 25; iteration++)
    {
        cacheStressReader.Open(fixture);
        while (cacheStressReader.ReadNext(out var frame))
            stressCache.Add(frame!);
        cacheStressReader.Close();
        True(stressCache.Count <= stressCache.Capacity, "bounded cache remains within capacity during stress");
    }
    True(stressCache.TryGetClosest(frames[^1].Timestamp, out var stressFrame), "stress cache retrieves final detached frame");
    Equal(frames[^1].Timestamp, stressFrame!.Timestamp, "stress cache retains final timestamp");
    stressCache.Dispose();
    stressCache.Dispose();
    Throws<ObjectDisposedException>(() => stressCache.TryGetClosest(0, out _), "disposed cache rejects lookup");
}

using (var vfrReader = new PortableVideoReader())
{
    vfrReader.Open(vfrFixture);
    var vfrFrames = new List<(long Timestamp, long Previous)>();
    while (vfrReader.ReadNext(out var frame))
        vfrFrames.Add((frame!.Timestamp, frame.PreviousTimestamp));
    long[] ffprobeTimestamps = ProbeTimestamps(vfrFixture);
    Equal(8, vfrFrames.Count, "VFR decoded frame count");
    True(vfrFrames.Select(frame => frame.Timestamp).SequenceEqual(ffprobeTimestamps), "VFR timestamps match ffprobe trace");
    True(vfrFrames.Zip(vfrFrames.Skip(1)).Select(pair => pair.Second.Timestamp - pair.First.Timestamp).Distinct().Count() > 1,
        "VFR fixture has nonuniform timestamp intervals");
    vfrReader.Seek(12288);
    True(vfrReader.ReadNext(out var vfrSeek), "VFR seek returns frame");
    Equal(12288L, vfrSeek!.Timestamp, "VFR exact seek timestamp");
    Equal(4096L, vfrSeek.PreviousTimestamp, "VFR seek previous timestamp comes from decoded preroll");
    vfrReader.Seek(22000);
    True(vfrReader.ReadNext(out var gapSeek), "VFR gap seek returns frame");
    Equal(36864L, gapSeek!.Timestamp, "VFR seek selects first frame at or after target gap");
}

using (var nonzeroStartReader = new PortableVideoReader())
{
    nonzeroStartReader.Open(nonzeroStartFixture);
    PortableVideoMetadata nonzeroMetadata = nonzeroStartReader.Metadata
        ?? throw new Exception("Nonzero-start metadata missing after open.");
    Equal(32768L, nonzeroMetadata.StartTimestamp, "nonzero-start stream start timestamp");
    long[] nonzeroProbeTimestamps = ProbeTimestamps(nonzeroStartFixture);
    var nonzeroFrames = new List<(long Timestamp, long Previous)>();
    while (nonzeroStartReader.ReadNext(out var frame))
        nonzeroFrames.Add((frame!.Timestamp, frame.PreviousTimestamp));
    True(nonzeroFrames.Select(frame => frame.Timestamp).SequenceEqual(nonzeroProbeTimestamps),
        "nonzero-start decoded timestamps match ffprobe trace");
    Equal(32768L, nonzeroFrames[0].Timestamp, "nonzero-start first presentation timestamp");
    Equal(long.MinValue, nonzeroFrames[0].Previous, "nonzero-start first previous timestamp");
    nonzeroStartReader.Seek(36864);
    True(nonzeroStartReader.ReadNext(out var exactNonzeroSeek), "nonzero-start exact seek returns frame");
    Equal(36864L, exactNonzeroSeek!.Timestamp, "nonzero-start exact seek timestamp");
    nonzeroStartReader.Seek(35000);
    True(nonzeroStartReader.ReadNext(out var gapNonzeroSeek), "nonzero-start gap seek returns frame");
    Equal(36864L, gapNonzeroSeek!.Timestamp, "nonzero-start gap seek selects first frame at or after target");
    nonzeroStartReader.Seek(32768);
    True(nonzeroStartReader.ReadNext(out var backwardNonzeroSeek), "nonzero-start backward seek returns frame");
    Equal(32768L, backwardNonzeroSeek!.Timestamp, "nonzero-start backward seek timestamp");
}

using (var failedOpenReader = new PortableVideoReader())
{
    Throws<InvalidDataException>(() => failedOpenReader.Open(Path.Combine(directory, "missing.mp4")), "missing input failure category");
    True(!failedOpenReader.IsOpen, "failed open leaves reader closed");
    failedOpenReader.Open(fixture);
    True(failedOpenReader.ReadNext(out _), "reader recovers after failed open");
}

using (var gifReader = new PortableVideoReader())
{
    gifReader.Open(gifFixture);
    PortableVideoMetadata gifMetadata = gifReader.Metadata ?? throw new Exception("GIF metadata missing after open.");
    Equal(32, gifMetadata.Width, "GIF width");
    Equal(24, gifMetadata.Height, "GIF height");
    Equal("gif", gifMetadata.CodecName, "GIF codec");
    gifReader.Seek(40);
    True(gifReader.ReadNext(out var gifSeek), "GIF exact seek returns frame");
    Equal(40L, gifSeek!.Timestamp, "GIF exact seek timestamp");
    gifReader.Seek(0);
    True(gifReader.ReadNext(out var gifBackwardSeek), "GIF backward seek returns frame");
    Equal(0L, gifBackwardSeek!.Timestamp, "GIF backward seek timestamp");
    gifReader.Close();
    gifReader.Open(gifFixture);
    var gifTimestamps = new List<long>();
    while (gifReader.ReadNext(out var frame))
    {
        Equal(PixelFormat.Rgb24, frame!.Format, "GIF RGB24 output");
        gifTimestamps.Add(frame.Timestamp);
    }
    Equal(5, gifTimestamps.Count, "GIF frame count");
    True(gifTimestamps.SequenceEqual([0L, 20L, 40L, 60L, 80L]), "GIF timestamps preserve FFmpeg centisecond timebase");
}

using (var svgReader = new PortableVideoReader())
{
    svgReader.Open(svgFixture);
    PortableVideoMetadata svgMetadata = svgReader.Metadata ?? throw new Exception("SVG metadata missing after open.");
    Equal(32, svgMetadata.Width, "SVG width");
    Equal(24, svgMetadata.Height, "SVG height");
    Equal("librsvg", svgMetadata.CodecName, "SVG decoder selected by local FFmpeg build");
    True(svgReader.ReadNext(out var svgFrame), "SVG rasterizes one frame through the selected FFmpeg backend");
    ReadOnlySpan<byte> svgRow = svgFrame!.GetRowSpan(svgFrame.Height / 2);
    Near(255, svgRow[0], "SVG left rectangle red");
    Near(0, svgRow[1], "SVG left rectangle green");
    int right = (svgFrame.Width - 1) * 3;
    Near(0, svgRow[right], "SVG right rectangle red");
    Near(255, svgRow[right + 1], "SVG right rectangle green");
    True(!svgReader.ReadNext(out _), "SVG has one decoded frame");
}

var decoderMeasurements = MeasureDecoder(fixture);
string fixtureSha = Hash(fixture);
string vfrFixtureSha = Hash(vfrFixture);
string nonzeroStartFixtureSha = Hash(nonzeroStartFixture);
Console.WriteLine($"PASS: {assertions} assertions; FFmpeg native={nativeVersions}; CFR sha256={fixtureSha}; VFR sha256={vfrFixtureSha}; nonzero-start sha256={nonzeroStartFixtureSha}; frames={frames.Count}; timestamps={string.Join(',', frames.Select(frame => frame.Timestamp))}; VFR timestamps={string.Join(',', ProbeTimestamps(vfrFixture))}; nonzero-start timestamps={string.Join(',', ProbeTimestamps(nonzeroStartFixture))}");
Console.WriteLine($"Decoder descriptive measurements (1 warmup + 5 samples): median={decoderMeasurements.MedianMilliseconds:0.###} ms per 8-frame decode; managed allocated={decoderMeasurements.ManagedBytesPerFrame:0.##} bytes/frame. Native allocations are not included.");

void Equal<T>(T expected, T actual, string label)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{label}: expected {expected}, got {actual}");
}

void True(bool condition, string label)
{
    assertions++;
    if (!condition) throw new Exception($"{label}: condition was false");
}

void Throws<T>(Action action, string label) where T : Exception
{
    assertions++;
    try { action(); }
    catch (T) { return; }
    throw new Exception($"{label}: expected {typeof(T).Name}");
}

void ColorNear((long Timestamp, long Previous, byte R, byte G, byte B) frame, int r, int g, int b, string label)
{
    Near(r, frame.R, label + " red");
    Near(g, frame.G, label + " green");
    Near(b, frame.B, label + " blue");
}

void Near(int expected, int actual, string label)
{
    assertions++;
    if (Math.Abs(expected - actual) > 15)
        throw new Exception($"{label}: expected near {expected}, got {actual}");
}

static void GenerateFixture(string path)
{
    var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
    string[] args = [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "color=c=red:s=64x48:r=4:d=0.5",
        "-f", "lavfi", "-i", "color=c=green:s=64x48:r=4:d=0.5",
        "-f", "lavfi", "-i", "color=c=blue:s=64x48:r=4:d=0.5",
        "-f", "lavfi", "-i", "color=c=yellow:s=64x48:r=4:d=0.5",
        "-filter_complex", "[0:v][1:v][2:v][3:v]concat=n=4:v=1:a=0,format=yuv420p[v]",
        "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "0", "-g", "8", "-bf", "2",
        "-fps_mode", "passthrough", "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", path
    ];
    foreach (string arg in args) start.ArgumentList.Add(arg);
    using Process process = Process.Start(start) ?? throw new Exception("Could not start ffmpeg to create the synthetic fixture.");
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"Could not generate fixture: {error}");
}

static void GenerateVfrFixture(string path)
{
    var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
    string[] args = [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "testsrc2=s=64x48:r=4:d=2",
        "-vf", "setpts='if(eq(N,0),0,if(eq(N,1),1,if(eq(N,2),3,if(eq(N,3),4,N+5))))'",
        "-c:v", "libx264", "-preset", "ultrafast", "-crf", "0", "-g", "8", "-bf", "2",
        "-fps_mode", "vfr", "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", path
    ];
    foreach (string arg in args) start.ArgumentList.Add(arg);
    using Process process = Process.Start(start) ?? throw new Exception("Could not start ffmpeg to create the VFR fixture.");
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"Could not generate VFR fixture: {error}");
}

static void GenerateNonzeroStartFixture(string path)
{
    var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
    string[] args = [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "testsrc2=s=64x48:r=4:d=1",
        "-vf", "setpts=PTS+2/TB",
        "-c:v", "libx264", "-preset", "ultrafast", "-crf", "0", "-g", "4", "-bf", "2",
        "-copyts", "-avoid_negative_ts", "disabled", "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", path
    ];
    foreach (string arg in args) start.ArgumentList.Add(arg);
    using Process process = Process.Start(start) ?? throw new Exception("Could not start ffmpeg to create the nonzero-start fixture.");
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"Could not generate nonzero-start fixture: {error}");
}

static void GenerateGifFixture(string path)
{
    var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
    string[] args = [
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", "testsrc2=s=32x24:r=5:d=1",
        "-loop", "0", path
    ];
    foreach (string arg in args) start.ArgumentList.Add(arg);
    using Process process = Process.Start(start) ?? throw new Exception("Could not start ffmpeg to create the GIF fixture.");
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"Could not generate GIF fixture: {error}");
}

static void GenerateSvgFixture(string path)
{
    File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"24\" viewBox=\"0 0 32 24\"><rect width=\"32\" height=\"24\" fill=\"#ff0000\"/><rect x=\"16\" width=\"16\" height=\"24\" fill=\"#00ff00\"/></svg>");
}

static long[] ProbeTimestamps(string path)
{
    var start = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    string[] args = ["-v", "error", "-select_streams", "v:0", "-show_entries", "frame=best_effort_timestamp", "-of", "csv=p=0", path];
    foreach (string arg in args) start.ArgumentList.Add(arg);
    using Process process = Process.Start(start) ?? throw new Exception("Could not start ffprobe for the timestamp trace.");
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"ffprobe timestamp trace failed: {error}");
    return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => long.Parse(line.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

static (double MedianMilliseconds, double ManagedBytesPerFrame) MeasureDecoder(string path)
{
    static (double Milliseconds, long ManagedBytes) Sample(string input)
    {
        using var reader = new PortableVideoReader();
        reader.Open(input);
        long before = GC.GetTotalAllocatedBytes(true);
        var stopwatch = Stopwatch.StartNew();
        int frames = 0;
        while (reader.ReadNext(out _)) frames++;
        stopwatch.Stop();
        long allocated = GC.GetTotalAllocatedBytes(true) - before;
        if (frames == 0) throw new Exception("Measurement fixture yielded no frames.");
        return (stopwatch.Elapsed.TotalMilliseconds, allocated / frames);
    }

    _ = Sample(path); // Warm native bindings, decoder, and shared memory pool.
    var samples = Enumerable.Range(0, 5).Select(_ => Sample(path)).ToArray();
    double[] orderedTimes = samples.Select(sample => sample.Milliseconds).Order().ToArray();
    double[] orderedAllocations = samples.Select(sample => (double)sample.ManagedBytes).Order().ToArray();
    return (orderedTimes[2], orderedAllocations[2]);
}

sealed class ManualTimeProvider : TimeProvider
{
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
}
