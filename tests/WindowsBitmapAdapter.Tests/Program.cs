using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kinovea.PortablePixels;
using Kinovea.PortablePixels.GdiPlus;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using PortablePixelFormat = Kinovea.PortablePixels.PixelFormat;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("WindowsBitmapAdapter.Tests requires Windows GDI+.");
    return 2;
}

string? captureDirectory = ReadOption(args, "--legacy-capture");
if (captureDirectory is null || !Directory.Exists(captureDirectory))
{
    Console.Error.WriteLine("Pass --legacy-capture <directory> from a successful .NET Framework 4.8 characterization run.");
    return 2;
}

var tests = new List<(string Name, Action Run)>
{
    ("24bpp BGR conversion preserves padding and logical row order", Test24Bpp),
    ("32bpp RGB conversion removes unused byte explicitly", Test32BppRgb),
    ("32bpp ARGB conversion preserves straight alpha", Test32BppArgb),
    ("32bpp PARGB conversion preserves premultiplied bytes", Test32BppPArgb),
    ("negative-stride Bitmap rows remain in logical order", TestNegativeStride),
    ("source Bitmap remains caller-owned and output is independent", TestOwnershipIndependence),
    ("LockBits is released after conversion and copy failure", TestLockRelease),
    ("unsupported pixel formats fail without disposing source", TestUnsupportedFormat)
};

List<LegacyCapture> captures;
try
{
    captures = LegacyCapture.ReadAndValidate(captureDirectory);
    foreach (LegacyCapture capture in captures)
        tests.Add(($"legacy reader capture converts: {capture.Id}", () => TestLegacyCapture(capture)));
tests.Add(("candidate fixture bytes match their provenance manifest", () => TestFixtureProvenance(captureDirectory)));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL legacy reader interchange: {ex}");
    return 1;
}

int failed = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {ex}");
    }
}

Console.WriteLine($"Adapter tests: {tests.Count - failed} passed, {failed} failed; legacy captures checked: {captures.Count}.");
if (args.Contains("--measure", StringComparer.Ordinal))
    AdapterMeasurements.Run();

return failed == 0 ? 0 : 1;

static string? ReadOption(string[] arguments, string option)
{
    for (int i = 0; i + 1 < arguments.Length; i++)
    {
        if (arguments[i] == option)
            return arguments[i + 1];
    }
    return null;
}

static void Test24Bpp()
{
    Color[] expected =
    [
        Color.FromArgb(255, 13, 71, 149), Color.FromArgb(255, 37, 101, 211), Color.FromArgb(255, 83, 29, 167),
        Color.FromArgb(255, 191, 43, 17), Color.FromArgb(255, 61, 173, 227), Color.FromArgb(255, 109, 59, 239)
    ];
    using var source = MakeBitmap(3, 2, GdiPixelFormat.Format24bppRgb, expected);
    int sourceStride = GetStride(source);
    Check(Math.Abs(sourceStride) > source.Width * 3, "24bpp fixture must have row padding.");

    using PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(source);
    Check(converted.Format == PortablePixelFormat.Rgb24, "Expected RGB24 output.");
    Check(converted.AlphaMode == AlphaMode.None, "RGB24 must not declare alpha.");
    Check(converted.StrideBytes == source.Width * 3, "Output is tightly packed RGB24.");
    Check(converted.Height == 2 && converted.Width == 3, "Dimensions must be preserved.");
    AssertBitmapPixels(source, converted, expected, hasAlpha: false);
    byte[] native = ReadNativeRows(source, out _);
    Check(native[0] == expected[0].B && native[1] == expected[0].G && native[2] == expected[0].R,
        "GDI+ Format24bppRgb raw bytes must be independently identified as B,G,R.");
}

static void Test32BppRgb()
{
    Color[] expected =
    [
        Color.FromArgb(255, 211, 23, 51), Color.FromArgb(255, 41, 197, 89),
        Color.FromArgb(255, 73, 107, 229), Color.FromArgb(255, 157, 61, 13)
    ];
    using var source = MakeBitmap(2, 2, GdiPixelFormat.Format32bppRgb, expected);
    byte[] native = ReadNativeRows(source, out _);
    Check(native[0] == expected[0].B && native[1] == expected[0].G && native[2] == expected[0].R,
        "GDI+ Format32bppRgb raw bytes must be B,G,R followed by an unused byte.");

    using PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(source);
    Check(converted.Format == PortablePixelFormat.Rgb24 && converted.AlphaMode == AlphaMode.None,
        "Format32bppRgb maps to RGB24 without inventing alpha.");
    AssertBitmapPixels(source, converted, expected, hasAlpha: false);
}

static void Test32BppArgb()
{
    Color[] expected =
    [
        Color.FromArgb(0, 0, 0, 0), Color.FromArgb(128, 80, 40, 24),
        Color.FromArgb(255, 13, 79, 151), Color.FromArgb(64, 120, 40, 8)
    ];
    using var source = MakeBitmap(2, 2, GdiPixelFormat.Format32bppArgb, expected);
    using PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(source);
    Check(converted.Format == PortablePixelFormat.Rgba32, "ARGB must map to RGBA32.");
    Check(converted.AlphaMode == AlphaMode.Straight, "Format32bppArgb is labelled straight alpha.");
    AssertBitmapPixels(source, converted, expected, hasAlpha: true);
    Check(converted.GetRowSpan(0)[7] == 128, "Partial alpha must remain unchanged.");
}

static void Test32BppPArgb()
{
    Color[] expected =
    [
        Color.FromArgb(128, 80, 40, 24), Color.FromArgb(255, 12, 78, 150)
    ];
    using var source = MakeBitmap(2, 1, GdiPixelFormat.Format32bppPArgb, expected);
    byte[] native = ReadNativeRows(source, out _);
    Check(native[0] == 12 && native[1] == 20 && native[2] == 40 && native[3] == 128,
        "Independently authored half-alpha sentinel must be premultiplied as B=12,G=20,R=40,A=128.");

    using PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(source);
    Check(converted.Format == PortablePixelFormat.Rgba32, "PARGB must map to RGBA32.");
    Check(converted.AlphaMode == AlphaMode.Premultiplied, "Format32bppPArgb must retain premultiplied labeling.");
    Check(converted.GetRowSpan(0).SequenceEqual(new byte[] { 40, 20, 12, 128, 12, 78, 150, 255 }),
        "PArgb bytes must be reordered without unpremultiplying or changing alpha.");
}

static void TestNegativeStride()
{
    using ExternalBitmap external = ExternalBitmap.CreateNegativeStride24Bpp();
    using PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(external.Bitmap);
    Check(converted.Format == PortablePixelFormat.Rgb24, "Expected RGB24 from negative-stride source.");
    Check(converted.GetRowSpan(0).SequenceEqual(new byte[] { 251, 31, 19, 67, 191, 43 }),
        "Logical first row should contain the top-row asymmetric sentinels.");
    Check(converted.GetRowSpan(1).SequenceEqual(new byte[] { 23, 83, 229, 149, 47, 211 }),
        "Logical second row should contain the distinct bottom-row sentinels.");
}

static void TestOwnershipIndependence()
{
    var source = MakeBitmap(2, 1, GdiPixelFormat.Format24bppRgb,
        [Color.FromArgb(255, 11, 33, 77), Color.FromArgb(255, 99, 121, 143)]);
    PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(source);
    Check(source.GetPixel(0, 0).ToArgb() == Color.FromArgb(255, 11, 33, 77).ToArgb(),
        "Conversion must not mutate source Bitmap pixels.");
    source.Dispose();
    Check(converted.GetRowSpan(0).SequenceEqual(new byte[] { 11, 33, 77, 99, 121, 143 }),
        "PixelBuffer must remain readable after source Bitmap disposal.");
    converted.Dispose();
    converted.Dispose();
    CheckThrows<ObjectDisposedException>(() => _ = converted.Memory, "disposed output rejects new memory views");
}

static void TestLockRelease()
{
    using var source = MakeBitmap(2, 2, GdiPixelFormat.Format24bppRgb,
        [Color.Red, Color.Green, Color.Blue, Color.White]);
    using (GdiPlusPixelBufferAdapter.Convert(source)) { }
    AssertCanLock(source);

    var shortOwner = new TestMemoryOwner(new byte[1]);
    CheckThrows<ArgumentException>(
        () => GdiPlusPixelBufferAdapter.ConvertWithOwnerFactory(source, _ => shortOwner),
        "short destination owner must fail during locked conversion");
    Check(shortOwner.DisposeCount == 1, "Failed conversion must dispose the untransferred destination owner once.");
    AssertCanLock(source);
    Check(source.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "Failed conversion must not mutate source pixels.");
}

static void TestUnsupportedFormat()
{
    using var source = new Bitmap(2, 2, GdiPixelFormat.Format8bppIndexed);
    CheckThrows<NotSupportedException>(() => GdiPlusPixelBufferAdapter.Convert(source),
        "indexed formats must fail explicitly");
    _ = source.GetPixel(0, 0);
}

static void TestLegacyCapture(LegacyCapture capture)
{
    using Bitmap source = capture.ReconstructBitmap();
    Check(source.Width == capture.Width && source.Height == capture.Height, "interchange dimensions");
    if (capture.PixelFormat != GdiPixelFormat.Format32bppPArgb)
        CheckBitmapMatchesRgba(source, capture.Rgba, "reconstructed actual-reader output");

    PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(source);
    source.Dispose();
    using (converted)
    {
        Check(converted.Width == capture.Width && converted.Height == capture.Height, "converted dimensions");
        Check(converted.Format == capture.ExpectedPortableFormat, "portable format interpretation");
        Check(converted.AlphaMode == capture.ExpectedAlphaMode, "alpha representation");
        ComparePortableToLegacyRgba(converted, capture.Rgba, capture.Native, capture.PixelFormat);
    }
}

static void TestFixtureProvenance(string root)
{
    string manifestPath = Path.Combine(root, "fixture-manifest.json");
    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
    JsonElement document = manifest.RootElement;
    Check(document.GetProperty("schemaVersion").GetInt32() == 1, "Fixture manifest schema must be version 1.");
    Check(document.GetProperty("approvalStatus").GetString() == "CANDIDATE_NOT_APPROVED",
        "Adapter test inputs must not promote or overwrite an approved baseline.");
    JsonElement fixtures = document.GetProperty("fixtures");
    Check(fixtures.GetArrayLength() == 14, "Expected the documented fourteen deterministic reader fixtures.");
    foreach (JsonElement fixture in fixtures.EnumerateArray())
    {
        string relative = fixture.GetProperty("fileName").GetString()!;
        string fixtureRoot = Path.Combine(root, "fixtures");
        string fullPath = Path.GetFullPath(Path.Combine(fixtureRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(fixtureRoot) + Path.DirectorySeparatorChar;
        Check(fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "Fixture path escaped the capture directory.");
        Check(File.Exists(fullPath), $"Fixture file is missing: {relative}");
        Check(Hash(File.ReadAllBytes(fullPath)) == fixture.GetProperty("sha256").GetString(),
            $"Fixture hash mismatch: {relative}");
    }
}

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static Bitmap MakeBitmap(int width, int height, GdiPixelFormat format, Color[] pixels)
{
    var bitmap = new Bitmap(width, height, format);
    try
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            bitmap.SetPixel(x, y, pixels[y * width + x]);
        return bitmap;
    }
    catch
    {
        bitmap.Dispose();
        throw;
    }
}

static void AssertBitmapPixels(Bitmap source, PixelBuffer converted, Color[] authored, bool hasAlpha)
{
    for (int y = 0; y < source.Height; y++)
    {
        ReadOnlySpan<byte> row = converted.GetRowSpan(y);
        for (int x = 0; x < source.Width; x++)
        {
            Color expected = authored[y * source.Width + x];
            Color observed = source.GetPixel(x, y);
            Check(observed.ToArgb() == expected.ToArgb(), $"GDI+ fixture sentinel at ({x},{y}) was not preserved.");
            int offset = x * (hasAlpha ? 4 : 3);
            Check(row[offset] == expected.R && row[offset + 1] == expected.G && row[offset + 2] == expected.B,
                $"RGB values differ at ({x},{y}).");
            if (hasAlpha)
                Check(row[offset + 3] == expected.A, $"Alpha differs at ({x},{y}).");
        }
    }
}

static int GetStride(Bitmap bitmap)
{
    var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
    BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, bitmap.PixelFormat);
    try { return data.Stride; }
    finally { bitmap.UnlockBits(data); }
}

static byte[] ReadNativeRows(Bitmap bitmap, out int stride)
{
    int bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
    int rowBytes = checked(bitmap.Width * bytesPerPixel);
    byte[] result = new byte[checked(rowBytes * bitmap.Height)];
    var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
    BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, bitmap.PixelFormat);
    try
    {
        stride = data.Stride;
        for (int y = 0; y < bitmap.Height; y++)
            Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), result, y * rowBytes, rowBytes);
    }
    finally { bitmap.UnlockBits(data); }
    return result;
}

static void AssertCanLock(Bitmap bitmap)
{
    var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
    BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, bitmap.PixelFormat);
    bitmap.UnlockBits(data);
}

static void CheckBitmapMatchesRgba(Bitmap bitmap, byte[] rgba, string label)
{
    Check(rgba.Length == checked(bitmap.Width * bitmap.Height * 4), $"{label}: RGBA size");
    for (int y = 0; y < bitmap.Height; y++)
    for (int x = 0; x < bitmap.Width; x++)
    {
        Color color = bitmap.GetPixel(x, y);
        int offset = (y * bitmap.Width + x) * 4;
        Check(color.R == rgba[offset] && color.G == rgba[offset + 1] && color.B == rgba[offset + 2] && color.A == rgba[offset + 3],
            $"{label}: pixel mismatch at ({x},{y})");
    }
}

static void ComparePortableToLegacyRgba(PixelBuffer buffer, byte[] rgba, byte[] native, GdiPixelFormat sourceFormat)
{
    bool hasAlpha = buffer.Format == PortablePixelFormat.Rgba32;
    Check(rgba.Length == checked(buffer.Width * buffer.Height * 4), "legacy RGBA payload length");
    int nativeBytesPerPixel = Image.GetPixelFormatSize(sourceFormat) / 8;
    for (int y = 0; y < buffer.Height; y++)
    {
        ReadOnlySpan<byte> row = buffer.GetRowSpan(y);
        for (int x = 0; x < buffer.Width; x++)
        {
            int expectedOffset = (y * buffer.Width + x) * 4;
            int nativeOffset = (y * buffer.Width + x) * nativeBytesPerPixel;
            int actualOffset = x * (hasAlpha ? 4 : 3);
            byte expectedR = sourceFormat == GdiPixelFormat.Format32bppPArgb ? native[nativeOffset + 2] : rgba[expectedOffset];
            byte expectedG = sourceFormat == GdiPixelFormat.Format32bppPArgb ? native[nativeOffset + 1] : rgba[expectedOffset + 1];
            byte expectedB = sourceFormat == GdiPixelFormat.Format32bppPArgb ? native[nativeOffset] : rgba[expectedOffset + 2];
            Check(row[actualOffset] == expectedR && row[actualOffset + 1] == expectedG && row[actualOffset + 2] == expectedB,
                $"legacy pixel RGB mismatch for {sourceFormat} at ({x},{y})");
            if (hasAlpha && sourceFormat == GdiPixelFormat.Format32bppArgb)
                Check(row[actualOffset + 3] == rgba[expectedOffset + 3], $"legacy pixel alpha mismatch at ({x},{y})");
            else if (hasAlpha && sourceFormat == GdiPixelFormat.Format32bppPArgb)
                Check(row[actualOffset + 3] == native[nativeOffset + 3], $"legacy premultiplied alpha mismatch at ({x},{y})");
            else if (!hasAlpha)
                Check(rgba[expectedOffset + 3] == 255, $"legacy opaque source has nonopaque alpha at ({x},{y})");
        }
    }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void CheckThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}: {message}");
}

sealed class TestMemoryOwner : IMemoryOwner<byte>
{
    private readonly byte[] bytes;
    public int DisposeCount { get; private set; }
    public TestMemoryOwner(byte[] bytes) => this.bytes = bytes;
    public Memory<byte> Memory => bytes;
    public void Dispose() => DisposeCount++;
}

sealed class ExternalBitmap : IDisposable
{
    private readonly IntPtr allocation;
    public Bitmap Bitmap { get; }

    private ExternalBitmap(IntPtr allocation, Bitmap bitmap)
    {
        this.allocation = allocation;
        Bitmap = bitmap;
    }

    public static ExternalBitmap CreateNegativeStride24Bpp()
    {
        const int width = 2;
        const int height = 2;
        const int physicalStride = 8;
        IntPtr allocation = Marshal.AllocHGlobal(physicalStride * height);
        try
        {
            IntPtr logicalTop = IntPtr.Add(allocation, physicalStride);
            byte[] topBgr = [19, 31, 251, 43, 191, 67];
            byte[] bottomBgr = [229, 83, 23, 211, 47, 149];
            Marshal.Copy(topBgr, 0, logicalTop, topBgr.Length);
            Marshal.Copy(bottomBgr, 0, IntPtr.Add(logicalTop, -physicalStride), bottomBgr.Length);
            var bitmap = new Bitmap(width, height, -physicalStride, GdiPixelFormat.Format24bppRgb, logicalTop);
            TestAssert.Check(bitmap.GetPixel(0, 0).ToArgb() == Color.FromArgb(255, 251, 31, 19).ToArgb(), "Negative-stride top sentinel is not top row.");
            TestAssert.Check(bitmap.GetPixel(0, 1).ToArgb() == Color.FromArgb(255, 23, 83, 229).ToArgb(), "Negative-stride bottom sentinel is not bottom row.");
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try { TestAssert.Check(data.Stride < 0, "GDI+ LockBits did not preserve the negative stride test fixture."); }
            finally { bitmap.UnlockBits(data); }
            return new ExternalBitmap(allocation, bitmap);
        }
        catch
        {
            Marshal.FreeHGlobal(allocation);
            throw;
        }
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        Marshal.FreeHGlobal(allocation);
    }
}

sealed class LegacyCapture
{
    public string Id { get; }
    public int Width { get; }
    public int Height { get; }
    public GdiPixelFormat PixelFormat { get; }
    public byte[] Native { get; }
    public byte[] Rgba { get; }
    public int NativeRowBytes { get; }
    public int LegacyStride { get; }
    private LegacyCapture(string id, int width, int height, GdiPixelFormat format, byte[] native, byte[] rgba,
        int nativeRowBytes, int stride)
    {
        Id = id; Width = width; Height = height; PixelFormat = format; Native = native; Rgba = rgba;
        NativeRowBytes = nativeRowBytes; LegacyStride = stride;
    }

    public PortablePixelFormat ExpectedPortableFormat => PixelFormat is GdiPixelFormat.Format24bppRgb or GdiPixelFormat.Format32bppRgb
        ? PortablePixelFormat.Rgb24 : PortablePixelFormat.Rgba32;
    public AlphaMode ExpectedAlphaMode => PixelFormat switch
    {
        GdiPixelFormat.Format24bppRgb or GdiPixelFormat.Format32bppRgb => AlphaMode.None,
        GdiPixelFormat.Format32bppArgb => AlphaMode.Straight,
        GdiPixelFormat.Format32bppPArgb => AlphaMode.Premultiplied,
        _ => throw new NotSupportedException($"No portable mapping for {PixelFormat}.")
    };

    public static List<LegacyCapture> ReadAndValidate(string root)
    {
        string reportPath = Path.Combine(root, "test-results.json");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        JsonElement document = report.RootElement;
        TestAssert.Check(document.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported reader interchange schema.");
        TestAssert.Check(document.GetProperty("status").GetString() == "PASS", "Legacy characterization did not pass.");
        TestAssert.Check(document.GetProperty("legacyReader").GetString() == "Kinovea.Video.Bitmap.VideoReaderBitmap", "Expected actual VideoReaderBitmap harness.");
        TestAssert.Check(document.GetProperty("caseCount").GetInt32() == 19 && document.GetProperty("assertionCount").GetInt32() == 537,
            "Legacy candidate report count differs from the reviewed characterization run.");

        var captures = new List<LegacyCapture>();
        foreach (JsonElement testCase in document.GetProperty("cases").EnumerateArray())
        {
            JsonElement observations = testCase.GetProperty("observations");
            foreach (JsonProperty property in observations.EnumerateObject())
                FindCaptures(property.Value, property.Name, root, captures);
        }
        TestAssert.Check(captures.Count >= 20, $"Expected multiple reader, rotation, reopen, and thumbnail captures; got {captures.Count}.");
        return captures;
    }

    private static void FindCaptures(JsonElement element, string id, string root, List<LegacyCapture> captures)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;
        if (element.TryGetProperty("nativeFile", out JsonElement nativeFile) && nativeFile.ValueKind == JsonValueKind.String &&
            element.TryGetProperty("rgbaFile", out JsonElement rgbaFile) && rgbaFile.ValueKind == JsonValueKind.String)
        {
            int width = element.GetProperty("width").GetInt32();
            int height = element.GetProperty("height").GetInt32();
            int nativeRowBytes = element.GetProperty("nativeRowBytes").GetInt32();
            int stride = element.GetProperty("strideBytes").GetInt32();
            string formatName = element.GetProperty("pixelFormat").GetString()!;
            GdiPixelFormat format = Enum.Parse<GdiPixelFormat>(formatName, ignoreCase: false);
            TestAssert.Check(element.GetProperty("nativeStorage").GetString() == "LockBits scanlines in logical top-to-bottom order; row padding omitted", $"Unexpected row contract for {id}.");
            TestAssert.Check(element.GetProperty("rgbaLayout").GetString() == "top-to-bottom, left-to-right, 8-bit R,G,B,A", $"Unexpected RGBA layout for {id}.");
            TestAssert.Check(stride != 0, $"Legacy capture {id} has zero stride.");
            TestAssert.Check(format is GdiPixelFormat.Format24bppRgb or GdiPixelFormat.Format32bppRgb or GdiPixelFormat.Format32bppArgb or GdiPixelFormat.Format32bppPArgb,
                $"Unsupported legacy captured format {format}.");

            string nativeRelative = nativeFile.GetString()!;
            string rgbaRelative = rgbaFile.GetString()!;
            string nativePath = ResolveRelative(root, nativeRelative);
            string rgbaPath = ResolveRelative(root, rgbaRelative);
            byte[] native = File.ReadAllBytes(nativePath);
            byte[] rgba = File.ReadAllBytes(rgbaPath);
            TestAssert.Check(Hash(native) == element.GetProperty("nativeSha256").GetString(), $"Raw capture hash mismatch: {nativeRelative}");
            TestAssert.Check(Hash(rgba) == element.GetProperty("rgbaSha256").GetString(), $"RGBA capture hash mismatch: {rgbaRelative}");
            int bpp = Image.GetPixelFormatSize(format) / 8;
            TestAssert.Check(nativeRowBytes == checked(width * bpp), $"Unexpected native row-byte count for {id}.");
            TestAssert.Check(native.Length == checked(nativeRowBytes * height), $"Native capture length differs for {id}.");
            TestAssert.Check(rgba.Length == checked(width * height * 4), $"RGBA capture length differs for {id}.");
            captures.Add(new LegacyCapture(id, width, height, format, native, rgba, nativeRowBytes, stride));
            return;
        }

        foreach (JsonProperty child in element.EnumerateObject())
            FindCaptures(child.Value, id + "." + child.Name, root, captures);
    }

    public Bitmap ReconstructBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat);
        try
        {
            var rectangle = new Rectangle(0, 0, Width, Height);
            BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat);
            try
            {
                int stride = data.Stride;
                TestAssert.Check(Math.Abs((long)stride) >= NativeRowBytes, $"Reconstructed bitmap stride too short for {Id}.");
                for (int y = 0; y < Height; y++)
                    Marshal.Copy(Native, y * NativeRowBytes, IntPtr.Add(data.Scan0, checked(y * stride)), NativeRowBytes);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static string ResolveRelative(string root, string relative)
    {
        TestAssert.Check(!Path.IsPathRooted(relative), "Capture path must be relative.");
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        TestAssert.Check(path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "Capture path escaped artifact directory.");
        return path;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

static class AdapterMeasurements
{
    public static void Run()
    {
        Measure(640, 480, GdiPixelFormat.Format24bppRgb);
        Measure(1920, 1080, GdiPixelFormat.Format32bppArgb);
    }

    private static void Measure(int width, int height, GdiPixelFormat format)
    {
        using Bitmap bitmap = CreateSource(width, height, format);
        for (int i = 0; i < 3; i++)
        {
            using PixelBuffer warmup = GdiPlusPixelBufferAdapter.Convert(bitmap);
        }

        const int iterations = 7;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var durations = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using PixelBuffer converted = GdiPlusPixelBufferAdapter.Convert(bitmap);
            timer.Stop();
            durations[i] = timer.Elapsed.TotalMilliseconds;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(durations);
        long destinationBytes = (long)width * height * (format == GdiPixelFormat.Format24bppRgb ? 3 : 4);
        Console.WriteLine($"Adapter sample: {width}x{height} {format}; iterations={iterations}; destination bytes/frame={destinationBytes}; managed bytes/conversion={(double)allocated / iterations:F0}; median ms={durations[iterations / 2]:F3}; samples ms=[{string.Join(",", durations.Select(x => x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}]");
    }

    private static Bitmap CreateSource(int width, int height, GdiPixelFormat format)
    {
        var bitmap = new Bitmap(width, height, format);
        var rectangle = new Rectangle(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, format);
        try
        {
            int bpp = Image.GetPixelFormatSize(format) / 8;
            int rowBytes = width * bpp;
            var row = new byte[rowBytes];
            for (int x = 0; x < width; x++)
            {
                int i = x * bpp;
                row[i] = 37;
                row[i + 1] = 83;
                row[i + 2] = 191;
                if (bpp == 4) row[i + 3] = 255;
            }
            for (int y = 0; y < height; y++)
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}

static class TestAssert
{
    public static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
