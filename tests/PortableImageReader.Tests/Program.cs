using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kinovea.PortableImageReader;
using Kinovea.PortablePixels;

internal static class Program
{
    private static int checks;

    private static int Main(string[] args)
    {
        try
        {
            string repoRoot = FindRepositoryRoot();
            string candidateRoot = Path.Combine(repoRoot,
                "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f");
            string? legacyCapture = GetOption(args, "--legacy-capture");

            VerifyCandidateHashes(candidateRoot);
            RunCandidateFixture(candidateRoot, "bmp-rgb-7x5", PixelFormat.Rgb24, AlphaMode.None,
                width: 7, height: 5, transparent: false);
            RunCandidateFixture(candidateRoot, "png-alpha-13x9", PixelFormat.Rgba32, AlphaMode.Straight,
                width: 13, height: 9, transparent: true);
            VerifyBmpPaddingAndOrientation();
            VerifyPngExifOrientations();

            if (legacyCapture is not null)
            {
                CompareActualLegacyCapture(legacyCapture, "bmp-rgb-7x5", PixelFormat.Rgb24, AlphaMode.None);
                CompareActualLegacyCapture(legacyCapture, "png-alpha-13x9", PixelFormat.Rgba32, AlphaMode.Straight);
                CompareOptionalPngExifCapture(legacyCapture);
            }

            VerifyFileAndBufferLifetime(candidateRoot);
            VerifyFailures(candidateRoot);

            if (args.Contains("--measure", StringComparer.Ordinal))
                MeasureDecoding();

            Console.WriteLine($"Portable image reader checks: PASS ({checks} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Portable image reader checks: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunCandidateFixture(
        string candidateRoot,
        string id,
        PixelFormat expectedFormat,
        AlphaMode expectedAlpha,
        int width,
        int height,
        bool transparent)
    {
        string fixturePath = Path.Combine(candidateRoot, "fixtures", id + (id.StartsWith("bmp-", StringComparison.Ordinal) ? ".bmp" : ".png"));
        string expectedPath = Path.Combine(candidateRoot, "pixels", id + ".rgba");
        byte[] candidatePixels = File.ReadAllBytes(expectedPath);

        using PixelBuffer frame = new PortableImageReader().Read(fixturePath);
        Equal(width, frame.Width, $"{id} width");
        Equal(height, frame.Height, $"{id} height");
        Equal(expectedFormat, frame.Format, $"{id} pixel format");
        Equal(expectedAlpha, frame.AlphaMode, $"{id} alpha mode");
        Equal(width * (expectedFormat == PixelFormat.Rgb24 ? 3 : 4), frame.StrideBytes, $"{id} stride");
        Equal(frame.StrideBytes * height, frame.Memory.Length, $"{id} owned byte count");

        int candidatePixelIndex = 0;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = frame.GetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                int channels = expectedFormat == PixelFormat.Rgb24 ? 3 : 4;
                int pixelOffset = x * channels;
                byte expectedRed = PatternChannel(x, y, 0);
                byte expectedGreen = PatternChannel(x, y, 1);
                byte expectedBlue = PatternChannel(x, y, 2);
                byte expectedOpacity = transparent ? (x == 0 && y == 0 ? (byte)0 : x == 1 && y == 0 ? (byte)64 : x == 2 && y == 0 ? (byte)128 : (byte)255) : (byte)255;

                Equal(expectedRed, row[pixelOffset], $"{id} ({x},{y}) red");
                Equal(expectedGreen, row[pixelOffset + 1], $"{id} ({x},{y}) green");
                Equal(expectedBlue, row[pixelOffset + 2], $"{id} ({x},{y}) blue");
                if (channels == 4)
                    Equal(expectedOpacity, row[pixelOffset + 3], $"{id} ({x},{y}) alpha");

                byte actualAlpha = channels == 4 ? row[pixelOffset + 3] : (byte)255;
                Equal(expectedRed, candidatePixels[candidatePixelIndex++], $"{id} candidate ({x},{y}) red");
                Equal(expectedGreen, candidatePixels[candidatePixelIndex++], $"{id} candidate ({x},{y}) green");
                Equal(expectedBlue, candidatePixels[candidatePixelIndex++], $"{id} candidate ({x},{y}) blue");
                Equal(actualAlpha, candidatePixels[candidatePixelIndex++], $"{id} candidate ({x},{y}) alpha");
            }
        }
        Equal(candidatePixels.Length, candidatePixelIndex, $"{id} candidate pixel count");
    }

    private static void VerifyBmpPaddingAndOrientation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kinovea-reader-padding-{Guid.NewGuid():N}.bmp");
        WriteBmp24(path, 3, 2);
        try
        {
            byte[] header = File.ReadAllBytes(path);
            Equal((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28, 2)), "authored BMP is 24-bit");
            Equal(12, ((3 * 3 + 3) / 4) * 4, "authored BMP scanline includes three pad bytes");
            using PixelBuffer frame = new PortableImageReader().Read(path);
            Equal(3, frame.Width, "padded BMP width");
            Equal(2, frame.Height, "padded BMP height");
            Equal(9, frame.StrideBytes, "padding is not included in logical row stride");
            for (int y = 0; y < 2; y++)
            {
                ReadOnlySpan<byte> row = frame.GetRowSpan(y);
                for (int x = 0; x < 3; x++)
                {
                    int offset = x * 3;
                    Equal(PatternChannel(x, y, 0), row[offset], $"padded BMP top-down ({x},{y}) red");
                    Equal(PatternChannel(x, y, 1), row[offset + 1], $"padded BMP top-down ({x},{y}) green");
                    Equal(PatternChannel(x, y, 2), row[offset + 2], $"padded BMP top-down ({x},{y}) blue");
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void VerifyPngExifOrientations()
    {
        // GDI+ did not expose the PNG eXIf orientation 6 property in the
        // fresh Windows legacy-reader characterization, so the observed
        // compatibility result is unchanged dimensions and raster order.
        const ushort orientation = 6;
        string path = Path.Combine(Path.GetTempPath(), $"kinovea-reader-exif-{orientation}-{Guid.NewGuid():N}.png");
        WritePngRgba(path, 3, 2, orientation);
        try
        {
            using PixelBuffer frame = new PortableImageReader().Read(path);
            Equal(3, frame.Width, "PNG eXIf orientation 6 legacy width");
            Equal(2, frame.Height, "PNG eXIf orientation 6 legacy height");
            Equal(PixelFormat.Rgba32, frame.Format, "PNG eXIf orientation 6 format");

            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    ReadOnlySpan<byte> row = frame.GetRowSpan(y);
                    int offset = x * 4;
                    Equal(PatternChannel(x, y, 0), row[offset], $"PNG EXIF {orientation} ({x},{y}) red");
                    Equal(PatternChannel(x, y, 1), row[offset + 1], $"PNG EXIF {orientation} ({x},{y}) green");
                    Equal(PatternChannel(x, y, 2), row[offset + 2], $"PNG EXIF {orientation} ({x},{y}) blue");
                    Equal((byte)255, row[offset + 3], $"PNG EXIF {orientation} ({x},{y}) alpha");
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (int X, int Y) MapLegacyOrientation(ushort orientation, int x, int y, int width, int height) => orientation switch
    {
        3 => (width - 1 - x, height - 1 - y),
        6 => (height - 1 - y, x),
        8 => (y, width - 1 - x),
        _ => (x, y)
    };

    private static void CompareActualLegacyCapture(string captureRoot, string id, PixelFormat format, AlphaMode alphaMode)
    {
        string reportPath = Path.Combine(captureRoot, "test-results.json");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        JsonElement captureCase = report.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "reader-" + id);

        JsonElement frame = captureCase.GetProperty("observations").GetProperty("frame");
        int width = frame.GetProperty("width").GetInt32();
        int height = frame.GetProperty("height").GetInt32();
        string rgbaPath = Path.Combine(captureRoot, frame.GetProperty("rgbaFile").GetString()!);
        byte[] expectedRgba = File.ReadAllBytes(rgbaPath);
        Equal(frame.GetProperty("rgbaSha256").GetString(), Sha256(rgbaPath), $"{id} actual reader capture SHA-256");

        string fixturePath = Path.Combine(captureRoot, "fixtures", id + (id.StartsWith("bmp-", StringComparison.Ordinal) ? ".bmp" : ".png"));
        Equal(GetFixtureSha256(captureRoot, id), Sha256(fixturePath), $"{id} actual reader fixture SHA-256");
        using PixelBuffer actual = new PortableImageReader().Read(fixturePath);
        Equal(width, actual.Width, $"{id} actual legacy dimensions width");
        Equal(height, actual.Height, $"{id} actual legacy dimensions height");
        Equal(format, actual.Format, $"{id} actual legacy format");
        Equal(alphaMode, actual.AlphaMode, $"{id} actual legacy alpha mode");
        CompareLogicalRgba(actual, expectedRgba, id + " actual legacy reader");
    }

    private static void CompareOptionalPngExifCapture(string captureRoot)
    {
        string reportPath = Path.Combine(captureRoot, "test-results.json");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        JsonElement captureCase = default;
        foreach (JsonElement item in report.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.GetProperty("name").GetString() == "reader-png-exif-orientation-06")
            {
                captureCase = item;
                break;
            }
        }

        if (captureCase.ValueKind == JsonValueKind.Undefined)
        {
            Console.WriteLine("PNG eXIf legacy comparison: NOT_RUN (the Windows characterization report has no PNG eXIf fixture).");
            return;
        }

        JsonElement frame = captureCase.GetProperty("observations").GetProperty("frame");
        string fixturePath = Path.Combine(captureRoot, "fixtures", "png-exif-orientation-06.png");
        string rgbaPath = Path.Combine(captureRoot, frame.GetProperty("rgbaFile").GetString()!);
        Equal(GetFixtureSha256(captureRoot, "png-exif-orientation-06"), Sha256(fixturePath), "PNG EXIF actual-reader fixture SHA-256");
        Equal(frame.GetProperty("rgbaSha256").GetString(), Sha256(rgbaPath), "PNG EXIF actual-reader pixels SHA-256");
        using PixelBuffer actual = new PortableImageReader().Read(fixturePath);
        Equal(frame.GetProperty("width").GetInt32(), actual.Width, "PNG EXIF actual-reader width");
        Equal(frame.GetProperty("height").GetInt32(), actual.Height, "PNG EXIF actual-reader height");
        Equal(PixelFormat.Rgba32, actual.Format, "PNG EXIF actual-reader format");
        Equal(AlphaMode.Straight, actual.AlphaMode, "PNG EXIF actual-reader alpha mode");
        CompareLogicalRgba(actual, File.ReadAllBytes(rgbaPath), "PNG EXIF actual legacy reader");
    }

    private static string GetFixtureSha256(string captureRoot, string id)
    {
        string manifestPath = Path.Combine(captureRoot, "fixture-manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement fixture = manifest.RootElement.GetProperty("fixtures")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);
        return fixture.GetProperty("sha256").GetString()!;
    }

    private static void CompareLogicalRgba(PixelBuffer actual, byte[] expectedRgba, string label)
    {
        int channels = actual.Format == PixelFormat.Rgb24 ? 3 : 4;
        Equal(actual.Width * actual.Height * 4, expectedRgba.Length, $"{label} RGBA data size");
        int index = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            ReadOnlySpan<byte> row = actual.GetRowSpan(y);
            for (int x = 0; x < actual.Width; x++)
            {
                int offset = x * channels;
                Equal(expectedRgba[index++], row[offset], $"{label} ({x},{y}) red");
                Equal(expectedRgba[index++], row[offset + 1], $"{label} ({x},{y}) green");
                Equal(expectedRgba[index++], row[offset + 2], $"{label} ({x},{y}) blue");
                byte expectedAlpha = expectedRgba[index++];
                if (channels == 3)
                    Equal((byte)255, expectedAlpha, $"{label} ({x},{y}) legacy opaque alpha");
                else
                    Equal(expectedAlpha, row[offset + 3], $"{label} ({x},{y}) alpha");
            }
        }
    }

    private static void VerifyCandidateHashes(string candidateRoot)
    {
        foreach (string line in File.ReadLines(Path.Combine(candidateRoot, "SHA256SUMS.txt")))
        {
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            string relativePath = parts[1].Replace("tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/", string.Empty, StringComparison.Ordinal);
            string extension = Path.GetExtension(relativePath);
            if (extension is not (".bmp" or ".png" or ".jpg" or ".gif" or ".rgba" or ".bin"))
                continue; // Text evidence can have platform-specific checkout line endings.

            string filePath = Path.Combine(candidateRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Equal(parts[0], Sha256(filePath), $"candidate binary SHA-256 {relativePath}");
        }
    }

    private static void VerifyFileAndBufferLifetime(string candidateRoot)
    {
        string source = Path.Combine(candidateRoot, "fixtures", "bmp-rgb-7x5.bmp");
        string temp = Path.Combine(Path.GetTempPath(), $"kinovea-reader-{Guid.NewGuid():N}.bmp");
        File.Copy(source, temp);
        PixelBuffer buffer;
        try
        {
            buffer = new PortableImageReader().Read(temp);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        string moved = temp + ".moved";
        File.Move(temp, moved);
        try
        {
            Equal((byte)17, buffer.GetRowSpan(0)[0], "buffer survives input file move");
            buffer.Dispose();
            buffer.Dispose();
            Throws<ObjectDisposedException>(() => _ = buffer.Memory, "disposed buffer rejects new memory access");
        }
        finally
        {
            buffer.Dispose();
            File.Delete(moved);
        }
    }

    private static void VerifyFailures(string candidateRoot)
    {
        string corrupt = Path.Combine(candidateRoot, "fixtures", "corrupt-image.png");
        Throws<InvalidDataException>(() => new PortableImageReader().Read(corrupt), "corrupt PNG exception");
        Throws<NotSupportedException>(() => new PortableImageReader().Read("fixture.gif"), "unsupported extension exception");
        Throws<ArgumentException>(() => new PortableImageReader().Read(" "), "empty path exception");
    }

    private static void MeasureDecoding()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"kinovea-reader-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            foreach ((int width, int height) in new[] { (640, 480), (1920, 1080) })
            {
                string bmp = Path.Combine(temp, $"{width}x{height}.bmp");
                string png = Path.Combine(temp, $"{width}x{height}.png");
                WriteBmp24(bmp, width, height);
                WritePngRgba(png, width, height);
                MeasureOne(bmp, "BMP", width, height);
                MeasureOne(png, "PNG", width, height);
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static void MeasureOne(string path, string format, int width, int height)
    {
        var reader = new PortableImageReader();
        for (int i = 0; i < 3; i++)
        {
            using PixelBuffer warm = reader.Read(path);
        }

        var times = new double[7];
        long allocated = 0;
        for (int i = 0; i < times.Length; i++)
        {
            long beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            using PixelBuffer decoded = reader.Read(path);
            watch.Stop();
            long afterAllocated = GC.GetAllocatedBytesForCurrentThread();
            allocated = afterAllocated - beforeAllocated;
            times[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        Console.WriteLine($"measure format={format} size={width}x{height} managedAllocatedBytes={allocated} samplesMs=[{string.Join(",", times.Select(t => t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}] medianMs={times[times.Length / 2]:F3}");
    }

    private static void WriteBmp24(string path, int width, int height)
    {
        int rowStride = checked((width * 3 + 3) & ~3);
        int imageLength = checked(rowStride * height);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(checked(54 + imageLength)); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(0); writer.Write(imageLength); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        var row = new byte[rowStride];
        for (int fileY = 0; fileY < height; fileY++)
        {
            int y = height - 1 - fileY;
            Array.Clear(row);
            for (int x = 0; x < width; x++)
            {
                int offset = x * 3;
                row[offset] = PatternChannel(x, y, 2);
                row[offset + 1] = PatternChannel(x, y, 1);
                row[offset + 2] = PatternChannel(x, y, 0);
            }
            writer.Write(row);
        }
    }

    private static void WritePngRgba(string path, int width, int height, ushort? orientation = null)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    zlib.WriteByte(PatternChannel(x, y, 0));
                    zlib.WriteByte(PatternChannel(x, y, 1));
                    zlib.WriteByte(PatternChannel(x, y, 2));
                    zlib.WriteByte(255);
                }
            }
        }

        using var output = File.Create(path);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WritePngChunk(output, "IHDR", ihdr);
        if (orientation.HasValue)
            WritePngChunk(output, "eXIf", MakeExifPayload(orientation.Value));
        WritePngChunk(output, "IDAT", compressed.ToArray());
        WritePngChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static byte[] MakeExifPayload(ushort orientation) => new byte[]
    {
        0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00,
        0x01, 0x00,
        0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
        (byte)(orientation & 0xff), (byte)(orientation >> 8), 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    private static void WritePngChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header[4..]);
        stream.Write(header);
        stream.Write(data);
        uint crc = Crc32(header[4..], data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = 0xffffffff;
        foreach (byte value in first) crc = UpdateCrc(crc, value);
        foreach (byte value in second) crc = UpdateCrc(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    private static byte PatternChannel(int x, int y, int channel) => channel switch
    {
        0 => (byte)((x * 37 + y * 11 + 17) & 255),
        1 => (byte)((x * 17 + y * 53 + 29) & 255),
        _ => (byte)((x * 91 + y * 7 + 43) & 255)
    };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kinovea.VS2019.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Kinovea repository root.");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == name) return Path.GetFullPath(args[i + 1]);
        return null;
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void Equal<T>(T expected, T actual, string message)
    {
        checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'.");
    }

    private static void Throws<TException>(Action action, string message) where TException : Exception
    {
        checks++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}.");
    }
}
