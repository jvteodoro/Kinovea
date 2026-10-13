using Kinovea.PortablePixels;
using Kinovea.PortableFrameGenerator;
using Kinovea.PortableReaderShell;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--host")
                return RunHost(args);

            string fixtures = Path.Combine(FindRepositoryRoot(),
                "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures");
            VerifyBmpHostFlow(fixtures);
            VerifyPngHostFlow(fixtures);
            VerifyFailures(fixtures);
            Console.WriteLine($"Portable still-image host: PASS ({assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Portable still-image host: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyBmpHostFlow(string fixtures)
    {
        using var host = new PortableReaderShell();
        Equal(PortableReaderState.Unopened, host.State, "initial host state");
        Equal(-1L, host.Current.Timestamp, "initial empty timestamp");
        Equal(-1L, host.Current.PreviousTimestamp, "initial empty previous timestamp");
        Equal(PortableOpenResult.Success, host.Open(Path.Combine(fixtures, "bmp-rgb-7x5.bmp")), "BMP selection/open");
        Equal(PortableReaderState.Open, host.State, "BMP open state");
        Equal(-1L, host.Current.Timestamp, "open does not create current frame");
        Equal(7, host.Metadata!.Width, "BMP metadata width");
        Equal(5, host.Metadata.Height, "BMP metadata height");
        Equal(25d, host.Metadata.FramesPerSecond, "legacy FPS metadata");
        Equal(250L, host.Metadata.DurationTimestamps, "legacy duration metadata");

        True(host.MoveRequest(next: true, target: -1), "first sequential frame request");
        PortableReaderFrame first = host.Current;
        Equal(0L, first.Timestamp, "first timestamp");
        Equal(-1L, first.PreviousTimestamp, "first previous timestamp");
        Equal(7, first.Image!.Width, "BMP frame width");
        Equal(5, first.Image.Height, "BMP frame height");
        Equal(PixelFormat.Rgb24, first.Image.Format, "BMP frame format");
        Equal(AlphaMode.None, first.Image.AlphaMode, "BMP alpha mode");
        Equal((byte)17, first.Image.GetRowSpan(0)[0], "authored BMP first pixel red");
        Equal((byte)29, first.Image.GetRowSpan(0)[1], "authored BMP first pixel green");
        Equal((byte)43, first.Image.GetRowSpan(0)[2], "authored BMP first pixel blue");
        True(host.PlayerRequest(PortablePlayerAction.Timestamp, 0), "repeat current request");
        Same(first, host.Current, "repeat preserves current wrapper identity");

        True(host.MoveRequest(next: true, target: -1), "next timestamp request");
        PortableReaderFrame next = host.Current;
        Equal(1L, next.Timestamp, "next timestamp");
        True(MemoryMarshal.TryGetArray(first.Image.Memory, out ArraySegment<byte> firstBacking), "first buffer exposes array owner");
        True(MemoryMarshal.TryGetArray(next.Image!.Memory, out ArraySegment<byte> nextBacking), "next buffer exposes array owner");
        Same(firstBacking.Array, nextBacking.Array, "navigation reuses decoded pixel storage");

        host.Close();
        Equal(PortableReaderState.Closed, host.State, "BMP close state");
        Throws<ObjectDisposedException>(() => _ = next.Image!.Memory, "close releases current pixel owner");
    }

    private static void VerifyPngHostFlow(string fixtures)
    {
        using var host = new PortableReaderShell();
        Equal(PortableOpenResult.Success, host.Open(Path.Combine(fixtures, "png-alpha-13x9.png")), "PNG selection/open");
        True(host.MoveRequest(next: true, target: -1), "PNG first frame");
        PortableImageFrame image = host.Current.Image!;
        Equal(13, image.Width, "PNG width");
        Equal(9, image.Height, "PNG height");
        Equal(PixelFormat.Rgba32, image.Format, "PNG frame format");
        Equal(AlphaMode.Straight, image.AlphaMode, "PNG alpha mode");
        Equal((byte)0, image.GetRowSpan(0)[3], "PNG transparent alpha");
        Equal((byte)64, image.GetRowSpan(0)[7], "PNG partial alpha 64");
        Equal((byte)128, image.GetRowSpan(0)[11], "PNG partial alpha 128");
        host.Close();
    }

    private static void VerifyFailures(string fixtures)
    {
        using var host = new PortableReaderShell();
        Equal(PortableOpenResult.NotSupported,
            host.Open(Path.Combine(fixtures, "unsupported.gif")), "unsupported extension status");
        Equal(PortableReaderState.FailedOpen, host.State, "unsupported extension state");
        Equal(PortableOpenResult.NotSupported,
            host.Open(Path.Combine(fixtures, "corrupt-image.png")), "corrupt BMP/PNG status");
        Equal(PortableReaderState.FailedOpen, host.State, "corrupt input state");
        Equal(-1L, host.Current.Timestamp, "failed open retains empty current");
    }

    private static int RunHost(string[] args)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 2 && args[1] is "-h" or "--help"))
        {
            Console.Error.WriteLine("Usage: PortableStillImageHost.Tests --host <bmp-or-png-path> [timestamp]");
            return args.Length == 2 ? 0 : 2;
        }
        if (args.Length == 3 && !long.TryParse(args[2], out _))
        {
            Console.Error.WriteLine("Timestamp must be an integer.");
            return 2;
        }

        long requestedTimestamp = args.Length == 3 ? long.Parse(args[2]) : 0;
        using var reader = new PortableReaderShell();
        PortableOpenResult result = reader.Open(args[1]);
        if (result != PortableOpenResult.Success)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = result.ToString(), path = args[1] }));
            return 1;
        }

        if (!reader.MoveRequest(next: true, target: -1)) return 1;
        if (requestedTimestamp != 0 && !reader.PlayerRequest(PortablePlayerAction.Timestamp, requestedTimestamp)) return 1;
        PortableReaderFrame frame = reader.Current;
        if (frame.Image is null || frame.Timestamp != requestedTimestamp) return 1;
        if (!reader.PlayerRequest(PortablePlayerAction.Timestamp, requestedTimestamp)) return 1;
        bool repeatedWrapperIdentity = ReferenceEquals(frame, reader.Current);
        string logicalPixelSha256;
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            for (int y = 0; y < frame.Image.Height; y++) hash.AppendData(frame.Image.GetRowSpan(y));
            logicalPixelSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        int probeX = frame.Image.Format == PixelFormat.Rgba32 ? 1 : 0;
        int bytesPerPixel = frame.Image.Format == PixelFormat.Rgba32 ? 4 : 3;
        ReadOnlySpan<byte> probeRow = frame.Image.GetRowSpan(0);
        int probeOffset = probeX * bytesPerPixel;
        byte red = probeRow[probeOffset];
        byte green = probeRow[probeOffset + 1];
        byte blue = probeRow[probeOffset + 2];
        byte alpha = bytesPerPixel == 4 ? probeRow[probeOffset + 3] : byte.MaxValue;

        reader.Close();
        bool newPixelAccessRejected = false;
        try { _ = frame.Image.Memory.Length; }
        catch (ObjectDisposedException) { newPixelAccessRejected = true; }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "Success",
            sourceSha = Environment.GetEnvironmentVariable("KINOVEA_SOURCE_SHA") ?? "LOCAL_UNRECORDED",
            host = "PortableStillImageHost test harness",
            reader = "PortableReaderShell -> PortableImageReader",
            extension = Path.GetExtension(args[1]),
            initialTimestamp = -1,
            timestamp = frame.Timestamp,
            previousTimestamp = frame.PreviousTimestamp,
            width = frame.Image.Width,
            height = frame.Image.Height,
            pixelFormat = frame.Image.Format.ToString(),
            alphaMode = frame.Image.AlphaMode.ToString(),
            pixelProbe = new { x = probeX, y = 0, red, green, blue, alpha },
            repeatedWrapperIdentity,
            logicalPixelSha256,
            stateAfterClose = reader.State.ToString(),
            newPixelAccessRejected
        }));
        return repeatedWrapperIdentity && newPixelAccessRejected ? 0 : 1;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kinovea.VS2019.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Kinovea.VS2019.sln from the test output directory.");
    }

    private static void True(bool value, string name)
    {
        assertions++;
        if (!value) throw new InvalidOperationException($"{name}: expected true.");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
    }

    private static void Same(object? expected, object? actual, string name)
    {
        assertions++;
        if (!ReferenceEquals(expected, actual)) throw new InvalidOperationException($"{name}: references differ.");
    }

    private static void Throws<TException>(Action action, string name) where TException : Exception
    {
        assertions++;
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
    }
}
