using System.Buffers;
using Kinovea.PortablePixels;

var tests = new (string Name, Action Run)[]
{
    ("Rgb24 exposes red-green-blue byte order", Rgb24ChannelOrder),
    ("synthetic pattern and padding are deterministic", SyntheticPixelsAndPadding),
    ("positive stride addresses top-down logical rows", PositiveStrideRows),
    ("negative stride uses explicit row-zero offset", NegativeStrideRows),
    ("RGBA alpha modes are labels and bytes are preserved", AlphaLabels),
    ("dimensions, format, alpha, and stride are validated", InvalidDescriptors),
    ("row and address arithmetic overflow is rejected", OverflowIsRejected),
    ("ownership transfers only after successful construction", OwnershipTransfer),
    ("disposal releases owner once and blocks new views", Disposal),
    ("previously copied memory view cannot be revoked", MemoryViewLifetimeCaveat)
};

int failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {exception}");
    }
}

Console.WriteLine($"Tests: {tests.Length - failures} passed, {failures} failed.");
if (failures != 0)
    return 1;

if (args.Contains("--measure", StringComparer.Ordinal))
    MeasureAllocations();

return 0;

static void Rgb24ChannelOrder()
{
    using PixelBuffer pixels = SyntheticPixelProducer.CreateRgb24(2, 1);
    Assert.Equal(PixelFormat.Rgb24, pixels.Format);
    Assert.Equal(AlphaMode.None, pixels.AlphaMode);
    Assert.SequenceEqual(
        new byte[] { 17, 29, 43, 48, 40, 62 },
        pixels.GetRowSpan(0).ToArray());
}

static void SyntheticPixelsAndPadding()
{
    using PixelBuffer first = SyntheticPixelProducer.CreateRgb24(3, 2, paddingBytes: 2);
    using PixelBuffer second = SyntheticPixelProducer.CreateRgb24(3, 2, paddingBytes: 2);
    Assert.Equal(11, first.StrideBytes);
    Assert.Equal(9, first.RowBytes);
    Assert.SequenceEqual(first.Memory.ToArray(), second.Memory.ToArray());
    Assert.SequenceEqual(new byte[] { 0xCD, 0xCD }, first.Memory.Span.Slice(9, 2).ToArray());
    Assert.SequenceEqual(new byte[] { 0xCD, 0xCD }, first.Memory.Span.Slice(20, 2).ToArray());
    Assert.SequenceEqual(new byte[]
    {
        SyntheticPixelProducer.Red(2, 1),
        SyntheticPixelProducer.Green(2, 1),
        SyntheticPixelProducer.Blue(2, 1)
    }, first.GetRowSpan(1).Slice(6, 3).ToArray());
}

static void PositiveStrideRows()
{
    var owner = new TrackingMemoryOwner(new byte[]
    {
        0xEE, 1, 2, 3, 4, 5, 6, 0xEE,
        7, 8, 9, 10, 11, 12, 0xEE, 0xEE
    });
    using PixelBuffer pixels = PixelBuffer.Create(2, 2, PixelFormat.Rgb24, AlphaMode.None, 7, 1, owner);
    Assert.Equal(1, pixels.RowZeroOffset);
    Assert.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, pixels.GetRowSpan(0).ToArray());
    Assert.SequenceEqual(new byte[] { 7, 8, 9, 10, 11, 12 }, pixels.GetRowSpan(1).ToArray());
    Assert.Throws<ArgumentOutOfRangeException>(() => pixels.GetRowSpan(2));
}

static void NegativeStrideRows()
{
    var owner = new TrackingMemoryOwner(new byte[]
    {
        7, 8, 9, 10, 11, 12, 0xEE,
        1, 2, 3, 4, 5, 6, 0xEE
    });
    using PixelBuffer pixels = PixelBuffer.Create(2, 2, PixelFormat.Rgb24, AlphaMode.None, -7, 7, owner);
    Assert.Equal(7, pixels.RowZeroOffset);
    Assert.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, pixels.GetRowSpan(0).ToArray());
    Assert.SequenceEqual(new byte[] { 7, 8, 9, 10, 11, 12 }, pixels.GetRowSpan(1).ToArray());
}

static void AlphaLabels()
{
    foreach (AlphaMode mode in new[] { AlphaMode.Straight, AlphaMode.Premultiplied })
    {
        var owner = new TrackingMemoryOwner(new byte[] { 0x21, 0x43, 0x65, 0x80 });
        using PixelBuffer pixels = PixelBuffer.Create(1, 1, PixelFormat.Rgba32, mode, 4, 0, owner);
        Assert.Equal(mode, pixels.AlphaMode);
        Assert.SequenceEqual(new byte[] { 0x21, 0x43, 0x65, 0x80 }, pixels.GetRowSpan(0).ToArray());
    }

    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(
        1, 1, PixelFormat.Rgba32, AlphaMode.None, 4, 0, new TrackingMemoryOwner(new byte[4])));
    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(
        1, 1, PixelFormat.Rgb24, AlphaMode.Straight, 3, 0, new TrackingMemoryOwner(new byte[3])));
}

static void InvalidDescriptors()
{
    Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(0, 1, PixelFormat.Rgb24, AlphaMode.None, 3, 0, new TrackingMemoryOwner(new byte[3])));
    Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(1, -1, PixelFormat.Rgb24, AlphaMode.None, 3, 0, new TrackingMemoryOwner(new byte[3])));
    Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(1, 1, (PixelFormat)123, AlphaMode.None, 3, 0, new TrackingMemoryOwner(new byte[3])));
    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(1, 1, PixelFormat.Rgb24, (AlphaMode)123, 3, 0, new TrackingMemoryOwner(new byte[3])));
    Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(2, 1, PixelFormat.Rgb24, AlphaMode.None, 5, 0, new TrackingMemoryOwner(new byte[6])));
    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(2, 2, PixelFormat.Rgb24, AlphaMode.None, 6, 0, new TrackingMemoryOwner(new byte[11])));
    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(2, 1, PixelFormat.Rgb24, AlphaMode.None, 6, -1, new TrackingMemoryOwner(new byte[6])));
    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(2, 2, PixelFormat.Rgb24, AlphaMode.None, -6, 0, new TrackingMemoryOwner(new byte[12])));
}

static void OverflowIsRejected()
{
    Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(
        int.MaxValue, 1, PixelFormat.Rgba32, AlphaMode.Straight, 1, 0, new TrackingMemoryOwner(Array.Empty<byte>())));
    Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(
        1, int.MaxValue, PixelFormat.Rgb24, AlphaMode.None, int.MaxValue, 0, new TrackingMemoryOwner(Array.Empty<byte>())));
    Assert.Throws<OverflowException>(() => SyntheticPixelProducer.CreateRgb24(int.MaxValue, 1));
    Assert.Throws<OverflowException>(() => SyntheticPixelProducer.CreateRgb24(1, int.MaxValue));
}

static void OwnershipTransfer()
{
    var rejectedTracker = new DisposalTracker();
    var rejected = new TrackingMemoryOwner(new byte[2], rejectedTracker);
    Assert.Throws<ArgumentException>(() => PixelBuffer.Create(1, 1, PixelFormat.Rgb24, AlphaMode.None, 3, 0, rejected));
    Assert.Equal(0, rejectedTracker.DisposeCount);
    rejected.Dispose();
    Assert.Equal(1, rejectedTracker.DisposeCount);

    var acceptedTracker = new DisposalTracker();
    PixelBuffer pixels = PixelBuffer.Create(1, 1, PixelFormat.Rgb24, AlphaMode.None, 3, 0,
        new TrackingMemoryOwner(new byte[3], acceptedTracker));
    pixels.Dispose();
    Assert.Equal(1, acceptedTracker.DisposeCount);
}

static void Disposal()
{
    var tracker = new DisposalTracker();
    PixelBuffer pixels = PixelBuffer.Create(1, 1, PixelFormat.Rgb24, AlphaMode.None, 3, 0,
        new TrackingMemoryOwner(new byte[] { 1, 2, 3 }, tracker));
    pixels.Dispose();
    pixels.Dispose();
    Assert.Equal(1, tracker.DisposeCount);
    Assert.Throws<ObjectDisposedException>(() => _ = pixels.Memory);
    Assert.Throws<ObjectDisposedException>(() => pixels.GetRowSpan(0).ToArray());
}

static void MemoryViewLifetimeCaveat()
{
    PixelBuffer pixels = PixelBuffer.Create(1, 1, PixelFormat.Rgb24, AlphaMode.None, 3, 0,
        new TrackingMemoryOwner(new byte[] { 9, 8, 7 }));
    ReadOnlyMemory<byte> borrowed = pixels.Memory;
    pixels.Dispose();
    // A copied ReadOnlyMemory is not revocable; use after disposal is prohibited by contract.
    Assert.SequenceEqual(new byte[] { 9, 8, 7 }, borrowed.ToArray());
}

static void MeasureAllocations()
{
    const int width = 640;
    const int height = 480;
    const int iterations = 100;
    const int warmupIterations = 256;
    for (int i = 0; i < warmupIterations; i++)
    {
        using PixelBuffer warmup = SyntheticPixelProducer.CreateRgb24(width, height);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < iterations; i++)
    {
        using PixelBuffer pixels = SyntheticPixelProducer.CreateRgb24(width, height);
        _ = pixels.GetRowSpan(height - 1)[^1];
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    long pixelBytes = (long)width * height * 3 * iterations;
    Console.WriteLine($"Allocation sample: {iterations} frames, {width}x{height} RGB24");
    Console.WriteLine($"Pixel payload bytes: {pixelBytes}; managed allocated bytes: {allocated}; non-payload allocation bytes: {allocated - pixelBytes}");
    Console.WriteLine("Pixel copies: 0 in producer and PixelBuffer.Create (source inspection); one byte[] allocation plus owner and PixelBuffer objects per frame.");
}

static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Sequences differ. Expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}

sealed class TrackingMemoryOwner : IMemoryOwner<byte>
{
    private readonly byte[] bytes;
    private readonly DisposalTracker tracker;

    public TrackingMemoryOwner(byte[] bytes, DisposalTracker? tracker = null)
    {
        this.bytes = bytes;
        this.tracker = tracker ?? new DisposalTracker();
    }

    public Memory<byte> Memory => bytes;
    public void Dispose() => tracker.DisposeCount++;
}

sealed class DisposalTracker
{
    public int DisposeCount { get; set; }
}
