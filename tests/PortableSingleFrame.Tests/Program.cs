using System.Runtime.InteropServices;
using System.Text.Json;
using Kinovea.PortableFrameGenerator;
using Kinovea.PortableReaderShell;
using Kinovea.PortableSingleFrame;
using ReaderShell = Kinovea.PortableReaderShell.PortableReaderShell;

int checks = 0;
string root = FindRoot();
string fixtures = Path.Combine(root, "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures");
string bmp = Path.Combine(fixtures, "bmp-rgb-7x5.bmp");
string corrupt = Path.Combine(fixtures, "corrupt-image.png");

using (var reader = new ReaderShell())
using (var cache = new PortableSingleFrameCache(reader))
{
    Same(null, cache.CurrentFrame, "initial cache slot");
    True(cache.IsEmpty, "initial cache empty");
    Equal(PortableOpenResult.Success, cache.Open(bmp), "open BMP");
    Same(null, cache.CurrentFrame, "open does not synthesize a cached frame");
    True(cache.MoveRequest(true, 123), "first sequential request");
    PortableReaderFrame first = NotNull(cache.CurrentFrame, "first cached frame");
    Equal(0L, first.Timestamp, "first timestamp");
    Equal(-1L, first.PreviousTimestamp, "first previous timestamp");
    Equal(17, first.Image!.GetRowSpan(0)[0], "first logical pixel byte");
    MemoryMarshal.TryGetArray(first.Image.Memory, out ArraySegment<byte> firstStorage);
    NotNull(firstStorage.Array, "first backing storage exists");

    True(cache.MoveRequest(false, 0), "repeat current timestamp");
    Same(first, cache.CurrentFrame, "repeat preserves wrapper identity");

    True(cache.MoveRequest(true, -500), "sequential advancement");
    PortableReaderFrame second = NotNull(cache.CurrentFrame, "second cached frame");
    NotSame(first, second, "changed timestamp replaces wrapper");
    Equal(1L, second.Timestamp, "second timestamp");
    Equal(0L, second.PreviousTimestamp, "second previous timestamp");
    Equal(17, first.Image.GetRowSpan(0)[0], "evicted wrapper remains readable while generator is open");
    MemoryMarshal.TryGetArray(second.Image!.Memory, out ArraySegment<byte> secondStorage);
    Same(firstStorage.Array, secondStorage.Array, "cache navigation reuses generator storage");

    cache.Clear();
    True(cache.IsEmpty, "Clear empties cache slot");
    Equal(17, second.Image.GetRowSpan(0)[0], "Clear does not dispose generator-owned storage");
    True(cache.MoveRequest(false, 1), "same timestamp after Clear repopulates cache");
    Same(second, cache.CurrentFrame, "Clear and repopulate reuses shell current wrapper");

    True(cache.MoveRequest(false, 249), "last timestamp accepted");
    PortableReaderFrame last = NotNull(cache.CurrentFrame, "last cached frame");
    Equal(249L, last.Timestamp, "last timestamp value");
    False(cache.MoveRequest(true, 0), "next past working-zone end rejected");
    Same(last, cache.CurrentFrame, "past-end request leaves cache identity unchanged");
    True(cache.MoveRequest(false, 250), "out-of-zone exact request returns true");
    Same(last, cache.CurrentFrame, "out-of-zone exact request leaves cache unchanged");
    True(cache.MoveRequest(false, -1), "below-zone exact request returns true");
    Same(last, cache.CurrentFrame, "below-zone exact request leaves cache unchanged");
    True(cache.PlayerRequest(PortablePlayerAction.Timestamp, 40), "timestamp player request");
    Equal(40L, NotNull(cache.CurrentFrame, "timestamp player frame").Timestamp, "timestamp player target preserved");
    True(cache.PlayerRequest(PortablePlayerAction.StepBackward, 249), "step-back player request");
    Equal(39L, NotNull(cache.CurrentFrame, "step-back frame").Timestamp, "step-back uses current plus legacy interval");
    cache.SetWorkingZone(0, 249);
    True(cache.MoveRequest(false, 249), "restore default zone after custom range");

    PortableReaderFrame beforeClose = last;
    cache.Close();
    True(cache.IsEmpty, "Close clears cache slot before owner close");
    Equal(PortableReaderState.Closed, cache.ReaderState, "Close closes reader");
    Throws<ObjectDisposedException>(() => beforeClose.Image!.GetRowSpan(0), "Close invalidates retained frame for new pixel access");
    cache.Close();

    Equal(PortableOpenResult.Success, cache.Open(bmp), "reopen after Close");
    True(cache.MoveRequest(false, 249), "same timestamp request after reopen");
    PortableReaderFrame reopened = NotNull(cache.CurrentFrame, "reopened frame");
    NotSame(beforeClose, reopened, "reopen refreshes old wrapper");
    NotSame(firstStorage.Array!, GetBackingArray(reopened.Image!), "reopen allocates independent decoded storage");

    PortableReaderFrame beforeFailure = reopened;
    Equal(PortableOpenResult.NotSupported, cache.Open(corrupt), "failed open result");
    True(cache.IsEmpty, "failed open leaves no cached disposed frame");
    Equal(PortableReaderState.FailedOpen, cache.ReaderState, "failed open state");
    Throws<ObjectDisposedException>(() => beforeFailure.Image!.GetRowSpan(0), "failed reopen releases prior reader storage");
    True(cache.MoveRequest(false, 249), "navigation after failed open preserves legacy method return");
    True(cache.IsEmpty, "failed-open navigation does not cache a fake frame");
    Equal(PortableOpenResult.Success, cache.Open(bmp), "recover after failed open");
    True(cache.MoveRequest(false, 249), "same timestamp request recovers frame");
    Equal(17, NotNull(cache.CurrentFrame, "recovery frame").Image!.GetRowSpan(0)[0], "recovery frame pixels");

    cache.Clear();
    True(cache.IsEmpty, "explicit Clear remains idempotent");
    True(cache.MoveRequest(false, 249), "cache can be repopulated after Clear");

    // Warm both paths before taking a descriptive allocation sample.
    cache.MoveRequest(false, 249);
    long repeatBefore = GC.GetAllocatedBytesForCurrentThread();
    cache.MoveRequest(false, 249);
    long repeatBytes = GC.GetAllocatedBytesForCurrentThread() - repeatBefore;
    long changedBefore = GC.GetAllocatedBytesForCurrentThread();
    cache.MoveRequest(false, 248);
    long changedBytes = GC.GetAllocatedBytesForCurrentThread() - changedBefore;
    Console.WriteLine($"Cache navigation allocation sample: repeated-current={repeatBytes} bytes; changed-timestamp={changedBytes} bytes; full-frame copies=0 by backing-array identity.");
}

// Cache Dispose detaches references but does not own or close the reader shell.
var disposeReader = new ReaderShell();
True(disposeReader.Open(bmp) == PortableOpenResult.Success, "separate owner open");
var disposableCache = new PortableSingleFrameCache(disposeReader);
True(disposableCache.MoveRequest(true, 0), "separate owner first frame");
PortableReaderFrame heldDuringCacheDispose = NotNull(disposableCache.CurrentFrame, "frame before cache dispose");
disposableCache.Dispose();
disposableCache.Dispose();
True(disposableCache.IsEmpty, "repeated cache Dispose empties slot");
Equal(17, heldDuringCacheDispose.Image!.GetRowSpan(0)[0], "cache Dispose does not release reader-owned pixels");
Equal(PortableReaderState.Open, disposableCache.ReaderState, "cache Dispose leaves reader open");
Throws<ObjectDisposedException>(() => disposableCache.MoveRequest(false, 1), "cache navigation after Dispose rejected");
disposeReader.Close();
Throws<ObjectDisposedException>(() => heldDuringCacheDispose.Image.GetRowSpan(0), "owner Close invalidates frame after cache Dispose");
disposeReader.Dispose();

// Closing the borrowed owner directly while a cache slot is populated is unsupported.
var bypassReader = new ReaderShell();
True(bypassReader.Open(bmp) == PortableOpenResult.Success, "bypass owner open");
var bypassCache = new PortableSingleFrameCache(bypassReader);
True(bypassCache.MoveRequest(true, 0), "bypass owner first frame");
PortableReaderFrame staleCached = NotNull(bypassCache.CurrentFrame, "cached frame before direct owner close");
bypassReader.Close();
Same(staleCached, bypassCache.CurrentFrame, "direct owner close leaves non-owning cache slot stale");
Throws<ObjectDisposedException>(() => staleCached.Image!.GetRowSpan(0), "direct owner close invalidates cached frame access");
bypassCache.Clear();
True(bypassCache.IsEmpty, "caller can clear stale slot after direct owner close");
bypassCache.Dispose();
bypassReader.Dispose();

Console.WriteLine($"Portable SingleFrame cache: PASS ({checks} assertions)");
Console.WriteLine("Ownership: no cache copies; Clear/Dispose detach only; reader Close releases decoded storage.");

string? tracePath = Option(args, "--legacy-cache-trace");
if (tracePath is not null)
    CompareLegacyTrace(tracePath);

void CompareLegacyTrace(string path)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement rootElement = document.RootElement;
    Equal(1, rootElement.GetProperty("schemaVersion").GetInt32(), "legacy cache trace schema version");
    Equal("CANDIDATE_NOT_APPROVED", rootElement.GetProperty("approvalStatus").GetString(), "legacy trace remains candidate");
    JsonElement sequence = rootElement.GetProperty("sequences").EnumerateArray()
        .Single(item => item.GetProperty("sequenceId").GetString() == "single-frame-ownership");
    Equal("isolated-contract-probe-no-production-construction-callsite-found", sequence.GetProperty("usageClassification").GetString(), "trace usage classification");
    JsonElement steps = sequence.GetProperty("steps");
    Equal(12, steps.GetArrayLength(), "legacy SingleFrame trace step count");
    Equal("Added", steps[0].GetProperty("result").GetString(), "legacy first insertion result");
    Equal(steps[0].GetProperty("currentFrameIdentity").GetInt32(), steps[1].GetProperty("currentFrameIdentity").GetInt32(), "legacy repeated current wrapper identity");
    Equal("error:System.ArgumentException", steps[3].GetProperty("externalPixelAccess").GetString(), "legacy Clear disposes external image reference");
    True(steps[6].GetProperty("externalPixelAccess").GetString()!.StartsWith("usable:", StringComparison.Ordinal), "custom no-op disposer preserves external image");
    Equal("error:System.ArgumentException", steps[7].GetProperty("pixelAccess").GetString(), "legacy replacement disposes reused generator bitmap");
    Equal("error:System.ArgumentException", steps[8].GetProperty("pixelAccess").GetString(), "generator Close invalidates cached bitmap");
    True(steps[9].GetProperty("pixelAccess").GetString()!.StartsWith("usable:", StringComparison.Ordinal), "fresh generator image usable after stale replacement");
    True(steps[10].GetProperty("isEmpty").GetBoolean(), "legacy Dispose clears populated slot");
    Equal("error:System.ArgumentException", steps[10].GetProperty("externalPixelAccess").GetString(), "legacy Dispose invalidates external image reference");
    True(steps[11].GetProperty("isEmpty").GetBoolean(), "repeated legacy Dispose remains empty");
    Console.WriteLine("Legacy SingleFrame trace comparison: PASS (14 assertions; candidate evidence)");
}

static byte[] GetBackingArray(PortableImageFrame frame)
{
    if (!MemoryMarshal.TryGetArray(frame.Memory, out ArraySegment<byte> segment) || segment.Array is null)
        throw new Exception("PixelBuffer is not backed by an accessible managed array in this test.");
    return segment.Array;
}

static string FindRoot()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Kinovea.VS2019.sln"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate repository root.");
}

static string? Option(string[] values, string name)
{
    int index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

void True(bool value, string name) => Equal(true, value, name);
void False(bool value, string name) => Equal(false, value, name);
void Same(object? expected, object? actual, string name) => Check(ReferenceEquals(expected, actual), name, "same reference");
void NotSame(object expected, object? actual, string name) => Check(!ReferenceEquals(expected, actual), name, "different reference");
T NotNull<T>(T? value, string name) where T : class { Check(value is not null, name, "non-null"); return value!; }
void Equal<T>(T expected, T actual, string name) => Check(EqualityComparer<T>.Default.Equals(expected, actual), name, $"expected {expected}, got {actual}");
void Throws<T>(Action action, string name) where T : Exception
{
    checks++;
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}: {name}");
}
void Check(bool pass, string name, string details)
{
    checks++;
    if (!pass) throw new Exception($"Assertion failed: {name} ({details})");
}
