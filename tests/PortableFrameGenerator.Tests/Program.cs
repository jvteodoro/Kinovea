using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kinovea.PortableFrameGenerator;
using Kinovea.PortablePixels;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            string root = FindRoot();
            string candidate = Path.Combine(root, "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f");
            CheckLifecycle(candidate, "bmp-rgb-7x5", ".bmp", 7, 5);
            CheckLifecycle(candidate, "png-alpha-13x9", ".png", 13, 9);
            CompareLegacyCapture(GetOption(args, "--legacy-capture"));
            Console.WriteLine($"Portable frame generator checks: PASS ({assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Portable frame generator checks: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckLifecycle(string candidate, string id, string extension, int width, int height)
    {
        string path = Path.Combine(candidate, "fixtures", id + extension);
        using var generator = new PortableImageFrameGenerator();
        Throws<InvalidOperationException>(() => generator.Generate(0), $"{id} generate before open");
        generator.Open(path);
        Check(generator.IsOpen, $"{id} open state");
        PortableImageFrame first = generator.Generate(0);
        PortableImageFrame repeated = generator.Generate(0);
        PortableImageFrame arbitrary = generator.Generate(37);
        Equal(0L, first.Timestamp, $"{id} initial timestamp");
        Equal(-1L, first.PreviousTimestamp, $"{id} initial previous timestamp");
        Equal(37L, arbitrary.Timestamp, $"{id} arbitrary timestamp");
        Equal(36L, arbitrary.PreviousTimestamp, $"{id} arbitrary previous timestamp");
        Check(!ReferenceEquals(first, repeated), $"{id} creates separate timestamp wrappers");
        Check(MemoryMarshal.TryGetArray(first.Memory, out ArraySegment<byte> firstSegment), $"{id} array-backed memory");
        Check(MemoryMarshal.TryGetArray(repeated.Memory, out ArraySegment<byte> repeatedSegment), $"{id} repeated array-backed memory");
        Check(ReferenceEquals(firstSegment.Array, repeatedSegment.Array), $"{id} reuses decoded backing storage");
        Equal(width, first.Width, $"{id} width");
        Equal(height, first.Height, $"{id} height");
        Equal(id.StartsWith("bmp", StringComparison.Ordinal) ? PixelFormat.Rgb24 : PixelFormat.Rgba32, first.Format, $"{id} format");
        Equal(id.StartsWith("bmp", StringComparison.Ordinal) ? AlphaMode.None : AlphaMode.Straight, first.AlphaMode, $"{id} alpha mode");
        Equal(id.StartsWith("bmp", StringComparison.Ordinal) ? 7 * 3 : 13 * 4, first.StrideBytes, $"{id} stride");
        VerifyExpectedPixels(first, id);

        generator.Close();
        Check(!generator.IsOpen, $"{id} closed state");
        Throws<ObjectDisposedException>(() => _ = first.Memory, $"{id} access after close");
        generator.Close();
        Throws<InvalidOperationException>(() => generator.Generate(1), $"{id} generate after close");
        generator.Open(path);
        PortableImageFrame reopened = generator.Generate(-5);
        Equal(-5L, reopened.Timestamp, $"{id} reopen timestamp");
        Check(!ReferenceEquals(firstSegment.Array, GetArray(reopened.Memory)), $"{id} reopen decodes independent storage");
        generator.Dispose();
        generator.Dispose();
        Throws<ObjectDisposedException>(() => generator.Open(path), $"{id} open after dispose");
        Throws<ObjectDisposedException>(() => _ = reopened.Memory, $"{id} access after dispose");
    }

    private static void VerifyExpectedPixels(PortableImageFrame frame, string id)
    {
        bool alpha = id.StartsWith("png", StringComparison.Ordinal);
        for (int y = 0; y < frame.Height; y++)
        {
            ReadOnlySpan<byte> row = frame.GetRowSpan(y);
            for (int x = 0; x < frame.Width; x++)
            {
                int pixel = x * (alpha ? 4 : 3);
                Equal((byte)((x * 37 + y * 11 + 17) & 255), row[pixel], $"{id} ({x},{y}) red");
                Equal((byte)((x * 17 + y * 53 + 29) & 255), row[pixel + 1], $"{id} ({x},{y}) green");
                Equal((byte)((x * 91 + y * 7 + 43) & 255), row[pixel + 2], $"{id} ({x},{y}) blue");
                if (alpha)
                {
                    byte expected = x == 0 && y == 0 ? (byte)0 : x == 1 && y == 0 ? (byte)64 : x == 2 && y == 0 ? (byte)128 : (byte)255;
                    Equal(expected, row[pixel + 3], $"{id} ({x},{y}) alpha");
                }
            }
        }
    }

    private static void CompareLegacyCapture(string? captureRoot)
    {
        if (captureRoot is null)
        {
            Console.WriteLine("Legacy frame comparison: NOT_RUN (no --legacy-capture supplied)");
            return;
        }

        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(captureRoot, "test-results.json")));
        foreach (string id in new[] { "bmp-rgb-7x5", "png-alpha-13x9" })
        {
            JsonElement item = report.RootElement.GetProperty("cases").EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == "reader-" + id);
            JsonElement observation = item.GetProperty("observations").GetProperty("frame");
            byte[] expected = File.ReadAllBytes(Path.Combine(captureRoot, observation.GetProperty("rgbaFile").GetString()!));
            Equal(observation.GetProperty("rgbaSha256").GetString()!, Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), $"{id} capture hash");
            string fixture = Path.Combine(captureRoot, "fixtures", id + (id.StartsWith("bmp", StringComparison.Ordinal) ? ".bmp" : ".png"));
            using var generator = new PortableImageFrameGenerator();
            generator.Open(fixture);
            PortableImageFrame actual = generator.Generate(0);
            Equal(observation.GetProperty("width").GetInt32(), actual.Width, $"{id} legacy width");
            Equal(observation.GetProperty("height").GetInt32(), actual.Height, $"{id} legacy height");
            CompareAsRgba(actual, expected, id);
            Console.WriteLine($"Legacy output comparison {id}: PASS ({actual.Width}x{actual.Height})");
        }
    }

    private static void CompareAsRgba(PortableImageFrame frame, byte[] expected, string id)
    {
        int index = 0;
        bool alpha = frame.Format == Kinovea.PortablePixels.PixelFormat.Rgba32;
        for (int y = 0; y < frame.Height; y++)
        {
            ReadOnlySpan<byte> row = frame.GetRowSpan(y);
            for (int x = 0; x < frame.Width; x++)
            {
                int p = x * (alpha ? 4 : 3);
                Equal(expected[index++], row[p], $"{id} legacy ({x},{y}) red");
                Equal(expected[index++], row[p + 1], $"{id} legacy ({x},{y}) green");
                Equal(expected[index++], row[p + 2], $"{id} legacy ({x},{y}) blue");
                Equal(expected[index++], alpha ? row[p + 3] : (byte)255, $"{id} legacy ({x},{y}) alpha");
            }
        }
        Equal(expected.Length, index, $"{id} legacy pixel length");
    }

    private static byte[]? GetArray(ReadOnlyMemory<byte> memory) => MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment) ? segment.Array : null;
    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Kinovea.VS2019.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
    private static string? GetOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }
    private static void Check(bool condition, string name) { assertions++; if (!condition) throw new InvalidOperationException("Assertion failed: " + name); }
    private static void Equal<T>(T expected, T actual, string name) { assertions++; if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Assertion failed: {name}; expected {expected}, got {actual}"); }
    private static void Throws<T>(Action action, string name) where T : Exception { assertions++; try { action(); } catch (T) { return; } throw new InvalidOperationException($"Assertion failed: {name}; expected {typeof(T).Name}"); }
}
