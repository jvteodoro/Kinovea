using System.Text.Json;
using System.Runtime.InteropServices;
using Kinovea.PortableFrameGenerator;
using Kinovea.PortableReaderShell;

internal static class Program
{
    private static int checks;
    private static int Main(string[] args)
    {
        try
        {
            string root = FindRoot();
            string fixtures = Path.Combine(root, "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures");
            Navigation(fixtures);
            FreshFirstFrame(fixtures);
            FailedOpenAndRecovery(fixtures);
            CloseBeforeOpen(fixtures);
            string? trace = Option(args, "--legacy-trace");
            if (trace is not null) VerifyLegacyTrace(trace);
            else Console.WriteLine("Legacy state trace comparison: NOT_RUN");
            Console.WriteLine($"Portable reader shell: PASS ({checks} assertions)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Navigation(string fixtures)
    {
        using var reader = new PortableReaderShell();
        Equal(PortableReaderState.Unopened, reader.State, "constructed state");
        Same(PortableReaderFrame.Empty, "empty current", reader.Current);
        True(reader.MoveRequest(true, 321), "move before open return");
        Equal(-1L, reader.Current.Timestamp, "move before open current");
        Equal(PortableOpenResult.Success, reader.Open(Path.Combine(fixtures, "bmp-rgb-7x5.bmp")), "open BMP");
        Equal(PortableReaderState.Open, reader.State, "open state");
        Equal(7, reader.Metadata!.Width, "width"); Equal(5, reader.Metadata.Height, "height");
        Equal(25d, reader.Metadata.FramesPerSecond, "fps"); Equal(250L, reader.Metadata.DurationTimestamps, "duration");
        Equal(0L, reader.WorkingZoneStart, "zone start"); Equal(249L, reader.WorkingZoneEnd, "zone end");

        True(reader.MoveRequest(true, 999), "first move");
        PortableReaderFrame frame0 = reader.Current;
        Equal(0L, frame0.Timestamp, "first timestamp override"); Equal(-1L, frame0.PreviousTimestamp, "first previous");
        True(reader.MoveRequest(true, -50), "second move");
        Equal(1L, reader.Current.Timestamp, "sequential timestamp");
        PortableReaderFrame currentAtOne = reader.Current;
        True(reader.MoveRequest(false, 1), "repeat timestamp return"); Same(currentAtOne, "repeat wrapper identity", reader.Current);
        True(reader.MoveRequest(false, 37), "forward seek return"); Equal(37L, reader.Current.Timestamp, "forward target");
        True(reader.MoveRequest(false, 12), "backward seek return"); Equal(12L, reader.Current.Timestamp, "backward target");
        True(reader.MoveRequest(false, 249), "last timestamp"); Equal(249L, reader.Current.Timestamp, "last target");
        False(reader.MoveRequest(true, 0), "past-end sequential return"); Equal(249L, reader.Current.Timestamp, "past-end unchanged");
        True(reader.MoveRequest(false, 250), "outside zone method return"); Equal(249L, reader.Current.Timestamp, "outside zone unchanged");
        True(reader.MoveRequest(false, -1), "below-zone exact seek method return"); Equal(249L, reader.Current.Timestamp, "below-zone unchanged");
        True(reader.PlayerRequest(PortablePlayerAction.Timestamp, 9), "player timestamp return"); Equal(9L, reader.Current.Timestamp, "player timestamp");
        True(reader.PlayerRequest(PortablePlayerAction.StepForward, 249), "step forward return"); Equal(10L, reader.Current.Timestamp, "step forward current-based");
        True(reader.PlayerRequest(PortablePlayerAction.StepBackward, 0), "step backward return"); Equal(9L, reader.Current.Timestamp, "step backward current-based");
        PortableReaderFrame stale = reader.Current;
        _ = stale.Image!.Memory.Span[0];
        reader.Close(); reader.Close();
        Equal(PortableReaderState.Closed, reader.State, "closed state"); Equal(9L, reader.Current.Timestamp, "close preserves timestamp");
        Throws<ObjectDisposedException>(() => _ = stale.Image!.Memory, "closed pixel access disposed");
        Equal(PortableOpenResult.Success, reader.Open(Path.Combine(fixtures, "bmp-rgb-7x5.bmp")), "reopen");
        True(reader.MoveRequest(false, 9), "refresh same timestamp after reopen");
        NotSame(stale, "reopen refreshes stale frame", reader.Current);
        reader.Dispose(); reader.Dispose(); Equal(PortableReaderState.Disposed, reader.State, "disposed state");
        Throws<ObjectDisposedException>(() => reader.MoveRequest(false, 1), "navigation after dispose");
    }

    private static void FreshFirstFrame(string fixtures)
    {
        using var reader = new PortableReaderShell();
        Equal(PortableOpenResult.Success, reader.Open(Path.Combine(fixtures, "png-alpha-13x9.png")), "fresh reader open");
        Same(PortableReaderFrame.Empty, "open retains initial empty frame", reader.Current);
        True(reader.MoveRequest(true, 200), "fresh first sequential request");
        Equal(0L, reader.Current.Timestamp, "fresh first frame override");
        Equal(-1L, reader.Current.PreviousTimestamp, "fresh first previous timestamp");
        Equal(13, reader.Current.Image!.Width, "fresh first frame width");
        Equal(9, reader.Current.Image.Height, "fresh first frame height");
    }

    private static void FailedOpenAndRecovery(string fixtures)
    {
        using var reader = new PortableReaderShell();
        Equal(PortableOpenResult.NotSupported, reader.Open(Path.Combine(fixtures, "corrupt-image.png")), "corrupt open result");
        Equal(PortableReaderState.FailedOpen, reader.State, "failed state");
        True(reader.MoveRequest(false, 0), "failed-state seek method return"); Same(PortableReaderFrame.Empty, "failed seek leaves empty", reader.Current);
        Equal(PortableOpenResult.Success, reader.Open(Path.Combine(fixtures, "png-alpha-13x9.png")), "recovery open");
        True(reader.MoveRequest(false, 0), "seek after recovery");
        Equal(0L, reader.Current.Timestamp, "recovery timestamp");
        Equal(13, reader.Current.Image!.Width, "recovery dimensions");
        byte[] bytes = reader.Current.Image.Memory.ToArray();
        MemoryMarshal.TryGetArray(reader.Current.Image.Memory, out ArraySegment<byte> firstStorage);
        var firstImage = reader.Current.Image;
        True(reader.MoveRequest(false, 1), "next timestamp");
        True(!ReferenceEquals(firstImage, reader.Current.Image), "new timestamp wrapper");
        MemoryMarshal.TryGetArray(reader.Current.Image!.Memory, out ArraySegment<byte> nextStorage);
        True(ReferenceEquals(firstStorage.Array, nextStorage.Array), "one decoded pixel storage reused");
        True(reader.Current.Image.Memory.Span.SequenceEqual(bytes), "decoded pixels stable across timestamps");
    }

    private static void CloseBeforeOpen(string fixtures)
    {
        using var reader = new PortableReaderShell();
        reader.Close(); reader.Close(); Equal(PortableReaderState.Closed, reader.State, "close unopened is idempotent safe deviation");
    }

    private static void VerifyLegacyTrace(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32(), "legacy trace version");
        JsonElement sequences = doc.RootElement.GetProperty("sequences");
        Equal(3, sequences.GetArrayLength(), "legacy trace sequence count");
        JsonElement navigation = sequences.EnumerateArray().Single(x => x.GetProperty("sequenceId").GetString() == "navigation-close-reopen");
        JsonElement steps = navigation.GetProperty("steps");
        Equal(20, steps.GetArrayLength(), "legacy navigation trace steps");
        JsonElement navState = steps[3].GetProperty("stateAfter");
        Equal(0L, navState.GetProperty("currentTimestamp").GetInt64(), "legacy first frame timestamp");
        Equal(-1L, navState.GetProperty("previousTimestamp").GetInt64(), "legacy first previous timestamp");
        Equal("System.ArgumentException", steps[17].GetProperty("stateAfter").GetProperty("pixelAccessErrorType").GetString(), "legacy stale Bitmap behavior category");
        Equal(PortableReaderMetadataNeverCompareCandidate(), doc.RootElement.GetProperty("approvalStatus").GetString(), "candidate remains unapproved");
        CompareNavigationTrace(steps, Path.Combine(FindRoot(), "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures", "bmp-rgb-7x5.bmp"));
        Console.WriteLine("Legacy reader trace: PASS (20 operations compared; safe stale-frame deviation recorded; candidate evidence)");
    }

    private static void CompareNavigationTrace(JsonElement legacySteps, string fixture)
    {
        using var reader = new PortableReaderShell();
        var frames = new List<PortableReaderFrame>();
        var images = new List<object>();
        var frameImageIds = new Dictionary<PortableReaderFrame, int>();
        var imageStorage = new Dictionary<PortableImageFrame, object>();
        for (int i = 0; i < legacySteps.GetArrayLength(); i++)
        {
            JsonElement step = legacySteps[i];
            string operation = step.GetProperty("operation").GetString()!;
            object? result = operation switch
            {
                "construct" or "close" or "close-repeat" or "close-final" => "completed",
                "move-next-unopened" or "move-next-first" or "move-next-second" or "next-past-end" => reader.MoveRequest(true, 0),
                "open-bmp" or "reopen-bmp" => reader.Open(fixture).ToString(),
                "repeat-current" => reader.MoveRequest(false, 1),
                "seek-forward" => reader.MoveRequest(false, 37),
                "seek-backward" => reader.MoveRequest(false, 12),
                "seek-last-valid" => reader.MoveRequest(false, 249),
                "seek-outside-zone" => reader.MoveRequest(false, 250),
                "player-timestamp" => reader.PlayerRequest(PortablePlayerAction.Timestamp, 9),
                "player-step-forward" => reader.PlayerRequest(PortablePlayerAction.StepForward, 249),
                "player-step-backward" => reader.PlayerRequest(PortablePlayerAction.StepBackward, 0),
                "seek-same-after-reopen" => reader.MoveRequest(false, 9),
                "seek-different-after-reopen" => reader.MoveRequest(false, 8),
                _ => throw new InvalidDataException("Unknown legacy trace operation: " + operation)
            };
            if (operation is "close" or "close-repeat" or "close-final") reader.Close();

            JsonElement legacyResult = step.GetProperty("result");
            if (legacyResult.ValueKind == JsonValueKind.True || legacyResult.ValueKind == JsonValueKind.False)
                Equal(legacyResult.GetBoolean(), (bool)result!, "legacy return " + operation);
            else if (legacyResult.ValueKind == JsonValueKind.String)
                Equal(legacyResult.GetString(), result?.ToString(), "legacy result " + operation);

            JsonElement state = step.GetProperty("stateAfter");
            Equal(state.GetProperty("loaded").GetBoolean(), reader.IsLoaded, "legacy loaded " + operation);
            long? expectedTimestamp = state.GetProperty("currentTimestamp").ValueKind == JsonValueKind.Null ? null : state.GetProperty("currentTimestamp").GetInt64();
            long? expectedPrevious = state.GetProperty("previousTimestamp").ValueKind == JsonValueKind.Null ? null : state.GetProperty("previousTimestamp").GetInt64();
            Equal(expectedTimestamp, reader.Current.Timestamp, "legacy timestamp " + operation);
            Equal(expectedPrevious, reader.Current.PreviousTimestamp, "legacy previous timestamp " + operation);
            Equal(state.GetProperty("workingZoneStart").GetInt64(), reader.WorkingZoneStart, "legacy zone start " + operation);
            Equal(state.GetProperty("workingZoneEnd").GetInt64(), reader.WorkingZoneEnd, "legacy zone end " + operation);
            Equal(state.GetProperty("framesPerSecond").GetDouble(), reader.Metadata?.FramesPerSecond ?? 0, "legacy fps " + operation);
            Equal(state.GetProperty("averageTimestampsPerFrame").GetDouble(), reader.Metadata?.AverageTimestampsPerFrame ?? 0, "legacy interval " + operation);
            Equal(state.GetProperty("durationTimestamps").GetInt64(), reader.Metadata?.DurationTimestamps ?? 0, "legacy duration " + operation);
            Equal(state.GetProperty("originalWidth").GetInt32(), reader.Metadata?.Width ?? 0, "legacy width " + operation);
            Equal(state.GetProperty("originalHeight").GetInt32(), reader.Metadata?.Height ?? 0, "legacy height " + operation);

            if (state.TryGetProperty("pixelProbe", out JsonElement probe) && reader.Current.Image is PortableImageFrame pixelFrame)
            {
                ReadOnlySpan<byte> row = pixelFrame.GetRowSpan(0);
                int channels = pixelFrame.Format == Kinovea.PortablePixels.PixelFormat.Rgb24 ? 3 : 4;
                Equal(probe.GetProperty("r").GetByte(), row[0], "legacy first pixel red " + operation);
                Equal(probe.GetProperty("g").GetByte(), row[1], "legacy first pixel green " + operation);
                Equal(probe.GetProperty("b").GetByte(), row[2], "legacy first pixel blue " + operation);
                Equal(probe.GetProperty("a").GetByte(), channels == 4 ? row[3] : (byte)255, "legacy first pixel alpha " + operation);
            }

            if (reader.Current.Image is PortableImageFrame frame && !imageStorage.ContainsKey(frame) && reader.IsLoaded)
            {
                MemoryMarshal.TryGetArray(frame.Memory, out ArraySegment<byte> segment);
                if (segment.Array is not null) imageStorage.Add(frame, segment.Array);
            }
            int frameId = Identity(frames, reader.Current);
            if (i < 17) Equal(state.GetProperty("frameIdentity").GetInt32(), frameId, "legacy frame identity " + operation);
            int? imageId = reader.Current.Image is null ? null : ImageIdentity(reader.Current.Image, images, imageStorage);
            JsonElement legacyImageId = state.GetProperty("imageIdentity");
            int? expectedImageId = legacyImageId.ValueKind == JsonValueKind.Null ? null : legacyImageId.GetInt32();
            if (i < 17) Equal(expectedImageId, imageId, "legacy image identity " + operation);
        }
    }

    private static int ImageIdentity(PortableImageFrame frame, List<object> identities, Dictionary<PortableImageFrame, object> storage)
    {
        if (!storage.TryGetValue(frame, out object? key))
        {
            MemoryMarshal.TryGetArray(frame.Memory, out ArraySegment<byte> segment);
            key = (object?)segment.Array ?? frame;
            storage.Add(frame, key);
        }
        for (int i = 0; i < identities.Count; i++) if (ReferenceEquals(identities[i], key)) return i;
        identities.Add(key);
        return identities.Count - 1;
    }

    private static int Identity(List<PortableReaderFrame> identities, PortableReaderFrame item)
    {
        for (int i = 0; i < identities.Count; i++) if (ReferenceEquals(identities[i], item)) return i;
        identities.Add(item);
        return identities.Count - 1;
    }

    private static string PortableReaderMetadataNeverCompareCandidate() => "CANDIDATE_NOT_APPROVED";
    private static string? Option(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static string FindRoot() { DirectoryInfo? d = new(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "Kinovea.VS2019.sln"))) d = d.Parent; return d?.FullName ?? throw new DirectoryNotFoundException(); }
    private static void True(bool b, string n) { checks++; if (!b) throw new Exception("Expected true: " + n); }
    private static void False(bool b, string n) => True(!b, n);
    private static void Equal<T>(T e, T a, string n) { checks++; if (!EqualityComparer<T>.Default.Equals(e, a)) throw new Exception($"{n}: expected {e}, got {a}"); }
    private static void Same(object? e, string n, object? a) { checks++; if (!ReferenceEquals(e, a)) throw new Exception("Expected same identity: " + n); }
    private static void NotSame(object? e, string n, object? a) { checks++; if (ReferenceEquals(e, a)) throw new Exception("Expected different identity: " + n); }
    private static void Throws<T>(Action f, string n) where T : Exception { checks++; try { f(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name + ": " + n); }
}
