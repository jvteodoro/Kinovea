using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using SystemBitmap = System.Drawing.Bitmap;
using Kinovea.Services;
using Kinovea.Video;
using Kinovea.Video.Bitmap;

namespace Kinovea.Bitmap.Characterization
{
    internal static class Program
    {
        private static readonly List<Dictionary<string, object>> Cases = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> Failures = new List<Dictionary<string, object>>();
        private static int assertionCount;
        private static int passedAssertions;

        private static int Main(string[] args)
        {
            string outputDirectory = GetOutputDirectory(args);
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(Path.Combine(outputDirectory, "fixtures"));
            Directory.CreateDirectory(Path.Combine(outputDirectory, "pixels"));

            string fatalError = null;
            List<FixtureSpec> fixtures = null;
            try
            {
                string assemblyPath = typeof(VideoReaderBitmap).Assembly.Location;
                EvidenceWriter.CaptureRuntimeMetadata(outputDirectory, assemblyPath);
                fixtures = FixtureFactory.Generate(Path.Combine(outputDirectory, "fixtures"));
                EvidenceWriter.WriteJson(
                    Path.Combine(outputDirectory, "fixture-manifest.json"),
                    new Dictionary<string, object>
                    {
                        { "schemaVersion", 1 },
                        { "approvalStatus", "CANDIDATE_NOT_APPROVED" },
                        { "generator", "FixtureFactory v1" },
                        { "fixtures", fixtures.Select(FixtureFactory.ToManifestEntry).ToArray() }
                    });

                foreach (FixtureSpec fixture in fixtures)
                {
                    if (fixture.Format == "INVALID")
                        RunCase("corrupt-input", delegate(CaseRecorder test) { CharacterizeCorruptInput(fixture, test); });
                    else if (fixture.Format != "UNSUPPORTED")
                        RunCase("reader-" + fixture.Id, delegate(CaseRecorder test) { CharacterizeFixture(fixture, outputDirectory, test); });
                }

                RunCase("reader-frame-lifecycle", delegate(CaseRecorder test)
                {
                    CharacterizeFrameLifecycle(fixtures.First(f => f.Id == "bmp-rgb-7x5"), test);
                });

                FixtureSpec rotationFixture = fixtures.First(f => f.Id == "bmp-rgb-7x5");
                foreach (ImageRotation rotation in Enum.GetValues(typeof(ImageRotation)))
                {
                    ImageRotation selectedRotation = rotation;
                    RunCase("manual-rotation-" + selectedRotation, delegate(CaseRecorder test)
                    {
                        CharacterizeManualRotation(rotationFixture, outputDirectory, selectedRotation, test);
                    });
                }
                RunCase("summary-thumbnail", delegate(CaseRecorder test) { CharacterizeSummary(fixtures.First(f => f.Id == "bmp-rgb-7x5"), outputDirectory, test); });
                FixtureSpec unsupported = fixtures.First(f => f.Id == "unsupported-gif");
                RunCase("unsupported-extension", delegate(CaseRecorder test) { CharacterizeUnsupportedExtension(unsupported, test); });
            }
            catch (Exception ex)
            {
                fatalError = ex.ToString();
                Failures.Add(new Dictionary<string, object> { { "case", "harness-fatal" }, { "error", fatalError } });
            }
            finally
            {
                var report = new Dictionary<string, object>
                {
                    { "schemaVersion", 1 },
                    { "generatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "status", Failures.Count == 0 ? "PASS" : "FAIL" },
                    { "baselineStatus", "CANDIDATE_NOT_APPROVED" },
                    { "legacyReader", "Kinovea.Video.Bitmap.VideoReaderBitmap" },
                    { "invocation", "VideoReaderBitmap.Open and public reader operations; returned VideoFrame.Image inspected" },
                    { "assertionCount", assertionCount },
                    { "passedAssertions", passedAssertions },
                    { "failedAssertions", assertionCount - passedAssertions },
                    { "caseCount", Cases.Count },
                    { "failedCaseCount", Cases.Count(c => (string)c["status"] != "PASS") },
                    { "fatalError", fatalError },
                    { "cases", Cases },
                    { "failures", Failures }
                };
                try
                {
                    EvidenceWriter.WriteJson(Path.Combine(outputDirectory, "test-results.json"), report);
                    File.WriteAllText(Path.Combine(outputDirectory, "test-results.txt"),
                        string.Format(CultureInfo.InvariantCulture,
                            "Status: {0}{1}Cases: {2}; failed cases: {3}{1}Assertions: {4}; passed: {5}; failed: {6}{1}",
                            Failures.Count == 0 ? "PASS" : "FAIL", Environment.NewLine, Cases.Count,
                            Cases.Count(c => (string)c["status"] != "PASS"), assertionCount, passedAssertions,
                            assertionCount - passedAssertions), new System.Text.UTF8Encoding(false));
                }
                catch (Exception reportError)
                {
                    Console.Error.WriteLine("Could not write final report: " + reportError);
                    fatalError = fatalError ?? reportError.ToString();
                }
            }

            Console.WriteLine("Characterization status: " + (Failures.Count == 0 ? "PASS" : "FAIL"));
            Console.WriteLine("Cases: " + Cases.Count.ToString(CultureInfo.InvariantCulture) + "; assertions: " + assertionCount.ToString(CultureInfo.InvariantCulture) + "; failed: " + (assertionCount - passedAssertions).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Evidence directory: " + outputDirectory);
            if (fatalError != null)
                Console.Error.WriteLine("Fatal characterization setup error: " + fatalError);
            return Failures.Count == 0 && fatalError == null ? 0 : 1;
        }

        private static void CharacterizeFrameLifecycle(FixtureSpec fixture, CaseRecorder test)
        {
            var reader = new VideoReaderBitmap();
            test.Equal(-1L, reader.Current.Timestamp, "initial current timestamp");
            test.Equal(-1L, reader.Current.PreviousTimestamp, "initial previous timestamp");
            test.Equal(true, reader.Current.Image == null, "initial image is null");
            test.Equal(OpenVideoResult.Success.ToString(), reader.Open(fixture.Path).ToString(), "open for lifecycle characterization");

            test.Equal(true, reader.MoveRequest(true, 123), "first sequential request accepted");
            VideoFrame zero = reader.Current;
            SystemBitmap shared = zero.Image;
            test.Equal(0L, zero.Timestamp, "first sequential timestamp");
            test.Equal(-1L, zero.PreviousTimestamp, "first sequential previous timestamp");
            test.Equal(true, reader.MoveRequest(true, 123), "second sequential request accepted");
            test.Equal(1L, reader.Current.Timestamp, "second sequential timestamp");
            test.Equal(0L, reader.Current.PreviousTimestamp, "second sequential previous timestamp");
            test.Equal(true, Object.ReferenceEquals(shared, reader.Current.Image), "sequential request reuses bitmap instance");

            test.Equal(true, reader.MoveRequest(false, 37), "forward arbitrary timestamp accepted");
            test.Equal(37L, reader.Current.Timestamp, "forward arbitrary timestamp value");
            test.Equal(36L, reader.Current.PreviousTimestamp, "forward arbitrary previous timestamp");
            test.Equal(true, reader.MoveRequest(false, 12), "backward arbitrary timestamp accepted");
            test.Equal(12L, reader.Current.Timestamp, "backward arbitrary timestamp value");
            test.Equal(11L, reader.Current.PreviousTimestamp, "backward arbitrary previous timestamp");
            VideoFrame atTwelve = reader.Current;
            test.Equal(true, reader.MoveRequest(false, 12), "same timestamp request accepted");
            test.Equal(true, Object.ReferenceEquals(atTwelve, reader.Current), "same timestamp retains VideoFrame instance");

            test.Equal(true, reader.MoveRequest(false, 249), "last in-zone timestamp accepted");
            test.Equal(249L, reader.Current.Timestamp, "last in-zone timestamp");
            SystemBitmap lastBitmap = reader.Current.Image;
            test.Equal(true, reader.MoveRequest(true, 0) == false, "next past working-zone end rejected");
            test.Equal(249L, reader.Current.Timestamp, "past-end next leaves frame unchanged");
            test.Equal(true, reader.MoveRequest(false, 250), "out-of-zone timestamp request method return");
            test.Equal(249L, reader.Current.Timestamp, "out-of-zone timestamp leaves frame unchanged");

            test.Equal(true, reader.PlayerRequest(new PlayerState(7, PlayerAction.StepBackward, true, 249)), "backward player request");
            test.Equal(248L, reader.Current.Timestamp, "backward player timestamp");
            test.Equal(247L, reader.Current.PreviousTimestamp, "backward player previous timestamp");
            test.Equal(true, Object.ReferenceEquals(lastBitmap, reader.Current.Image), "DisposePrevious does not release shared bitmap");

            reader.Close();
            test.Equal(false, reader.Loaded, "closed loaded state");
            bool disposedBitmap;
            try { int ignored = lastBitmap.Width; disposedBitmap = false; }
            catch (ArgumentException) { disposedBitmap = true; }
            test.Equal(true, disposedBitmap, "Close disposes shared bitmap");
            test.Equal(248L, reader.Current.Timestamp, "Close retains current frame wrapper");

            test.Equal(OpenVideoResult.Success.ToString(), reader.Open(fixture.Path).ToString(), "reopen result");
            test.Equal(true, reader.MoveRequest(false, 248), "same-position request after reopen");
            test.Equal(true, Object.ReferenceEquals(lastBitmap, reader.Current.Image), "same-position reopen retains old bitmap reference");
            try { int ignored = reader.Current.Image.Width; test.Observation("sameTimestampReopenAccess", "accessible width=" + ignored); }
            catch (Exception ex) { test.Observation("sameTimestampReopenAccess", ex.GetType().FullName); }
            test.Equal(true, reader.MoveRequest(false, 247), "different-position request after reopen");
            test.Equal(247L, reader.Current.Timestamp, "different-position reopen timestamp");
            test.Equal(false, Object.ReferenceEquals(lastBitmap, reader.Current.Image), "different-position reopen creates fresh bitmap");
            reader.Close();

            var generator = new FrameGeneratorImageFile();
            test.Equal(OpenVideoResult.Success.ToString(), generator.Open(fixture.Path).ToString(), "standalone image generator open");
            SystemBitmap generated = generator.Generate(0);
            generator.DisposePrevious(generated);
            test.Equal(true, generated.Width > 0, "DisposePrevious leaves generated image alive");
            generator.Close();
            try { int ignored = generated.Width; disposedBitmap = false; }
            catch (ArgumentException) { disposedBitmap = true; }
            test.Equal(true, disposedBitmap, "generator Close disposes generated image");

            var cacheGenerator = new FrameGeneratorImageFile();
            cacheGenerator.Open(fixture.Path);
            SystemBitmap cached = cacheGenerator.Generate(0);
            var single = new SingleFrame();
            single.Add(new VideoFrame(cached, 0, -1));
            single.Clear();
            try { int ignored = cached.Width; disposedBitmap = false; }
            catch (ArgumentException) { disposedBitmap = true; }
            test.Equal(true, disposedBitmap, "default SingleFrame eviction disposes shared generator bitmap");
            SystemBitmap returnedAgain = cacheGenerator.Generate(1);
            test.Equal(true, Object.ReferenceEquals(cached, returnedAgain), "generator returns cached bitmap after cache eviction");
            test.Observation("cacheEviction", "SingleFrame.Clear disposed the generator-owned bitmap; the generator still returned that same disposed instance.");
            cacheGenerator.Close();
            single.Dispose();
        }

        private static string GetOutputDirectory(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--output" && i + 1 < args.Length)
                    return Path.GetFullPath(args[i + 1]);
            }
            return Path.GetFullPath(Path.Combine("artifacts", "bitmap-characterization"));
        }

        private static void RunCase(string name, Action<CaseRecorder> action)
        {
            var recorder = new CaseRecorder(name);
            try
            {
                action(recorder);
            }
            catch (Exception ex)
            {
                recorder.Error = ex.ToString();
                Failures.Add(new Dictionary<string, object> { { "case", name }, { "error", recorder.Error } });
            }

            var report = recorder.ToDictionary();
            Cases.Add(report);
            if ((string)report["status"] != "PASS" && recorder.Error == null)
                Failures.Add(new Dictionary<string, object> { { "case", name }, { "error", "One or more explicit assertions failed." } });
        }

        private static void CharacterizeFixture(FixtureSpec fixture, string outputDirectory, CaseRecorder test)
        {
            var reader = new VideoReaderBitmap();
            {
                OpenVideoResult openResult = reader.Open(fixture.Path);
                test.Equal(OpenVideoResult.Success.ToString(), openResult.ToString(), "open result");
                test.Equal(true, reader.Loaded, "loaded after open");
                test.Equal(fixture.Width + "x" + fixture.Height,
                    reader.Info.OriginalSize.Width + "x" + reader.Info.OriginalSize.Height, "original dimensions");
                test.Equal((int)fixture.ExpectedQuarterTurns.Value, (int)reader.Info.OriginalRotation, "EXIF-derived rotation quarter turns");
                test.Equal(25.0, reader.Info.FramesPerSeconds, "reported frame rate");
                test.Equal(0L, reader.Info.FirstTimeStamp, "reported first timestamp");
                test.Equal(0L, reader.Info.LastTimeStamp, "reported last timestamp field");
                test.Equal(1.0, reader.Info.AverageTimeStampsPerFrame, "reported average timestamps per frame");
                test.Equal(250L, reader.Info.DurationTimeStamps, "reported duration timestamps");
                test.Equal(40.0, reader.Info.FrameIntervalMilliseconds, "reported frame interval milliseconds");
                test.Equal("[0 --> 249]", reader.WorkingZone.ToString(), "initial working zone");
                test.Observation("flags", reader.Flags.ToString());
                test.Observation("decodingMode", reader.DecodingMode.ToString());
                test.Equal(true, String.Equals(Path.GetFullPath(fixture.Path), Path.GetFullPath(reader.Info.FilePath), StringComparison.OrdinalIgnoreCase), "reader reports opened input path");
                test.Observation("reportedLastTimestamp", reader.Info.LastTimeStamp);

                bool firstMove = reader.MoveRequest(true, 12345);
                test.Equal(true, firstMove, "first next move accepted");
                test.Equal(0L, reader.Current.Timestamp, "first next move selects timestamp zero");
                test.Equal(-1L, reader.Current.PreviousTimestamp, "first frame previous timestamp");
                test.True(reader.Current.Image != null, "current frame image exists");
                if (reader.Current.Image != null)
                {
                    int expectedWidth = fixture.ExpectedQuarterTurns == 1 || fixture.ExpectedQuarterTurns == 3 ? fixture.Height : fixture.Width;
                    int expectedHeight = fixture.ExpectedQuarterTurns == 1 || fixture.ExpectedQuarterTurns == 3 ? fixture.Width : fixture.Height;
                    test.Equal(expectedWidth + "x" + expectedHeight,
                        reader.Current.Image.Width + "x" + reader.Current.Image.Height, "decoded dimensions after EXIF rotation");
                    test.Observation("frame", CaptureBitmap(reader.Current.Image, fixture.Id, outputDirectory, test));

                    if (fixture.Id == "png-alpha-13x9")
                    {
                        test.Equal(0, reader.Current.Image.GetPixel(0, 0).A, "transparent PNG pixel alpha");
                        test.Equal(128, reader.Current.Image.GetPixel(2, 0).A, "partially transparent PNG pixel alpha");
                    }
                    if (fixture.Id == "bmp-rgb-7x5")
                    {
                        Color expected = FixtureFactory.PatternPixel(0, 0, false);
                        Color actual = reader.Current.Image.GetPixel(0, 0);
                        test.Equal(expected.R, actual.R, "BMP first pixel red channel");
                        test.Equal(expected.G, actual.G, "BMP first pixel green channel");
                        test.Equal(expected.B, actual.B, "BMP first pixel blue channel");
                        test.Equal(255, actual.A, "BMP first pixel alpha channel");
                    }

                    SystemBitmap firstImage = reader.Current.Image;
                    bool timestampMove = reader.MoveRequest(false, 37);
                    test.Equal(true, timestampMove, "arbitrary timestamp move accepted");
                    test.Equal(37L, reader.Current.Timestamp, "arbitrary timestamp stored");
                    test.Equal(36L, reader.Current.PreviousTimestamp, "arbitrary timestamp previous timestamp");
                    test.Equal(true, Object.ReferenceEquals(firstImage, reader.Current.Image), "generator returns same image object for timestamps");

                    test.Equal(true, reader.PlayerRequest(new PlayerState(1, PlayerAction.Timestamp, true, 80)), "player timestamp request accepted");
                    test.Equal(80L, reader.Current.Timestamp, "player timestamp request position");
                    test.Equal(79L, reader.Current.PreviousTimestamp, "player timestamp previous position");
                    test.Equal(true, reader.PlayerRequest(new PlayerState(2, PlayerAction.StepForward, true, 80)), "step-forward request accepted");
                    test.Equal(81L, reader.Current.Timestamp, "step-forward timestamp");
                    test.Equal(true, reader.PlayerRequest(new PlayerState(3, PlayerAction.StepBackward, true, 81)), "step-backward request accepted");
                    test.Equal(80L, reader.Current.Timestamp, "step-backward timestamp");

                    bool outsideMove = reader.MoveRequest(false, 250);
                    test.Equal(true, outsideMove, "out-of-zone request method return");
                    test.Equal(80L, reader.Current.Timestamp, "out-of-zone request leaves current frame unchanged");
                    test.Equal("[0 --> 249]", reader.WorkingZone.ToString(), "working zone after out-of-zone request");

                    reader.Close();
                    test.Equal(false, reader.Loaded, "loaded after close");
                    try
                    {
                        int ignoredWidth = reader.Current.Image.Width;
                        test.Observation("closedImageAccess", "returned width=" + ignoredWidth.ToString(CultureInfo.InvariantCulture));
                    }
                    catch (Exception ex)
                    {
                        test.Observation("closedImageAccess", ex.GetType().FullName + ": " + ex.Message);
                    }

                    OpenVideoResult reopen = reader.Open(fixture.Path);
                    test.Equal(OpenVideoResult.Success.ToString(), reopen.ToString(), "reopen result");
                    test.Equal(true, reader.Loaded, "loaded after reopen");
                    bool reopenMove = reader.MoveRequest(false, 81);
                    test.Equal(true, reopenMove, "reopened reader timestamp request accepted");
                    test.Equal(81L, reader.Current.Timestamp, "reopened frame timestamp");
                    if (reader.Current.Image != null)
                        test.Observation("reopenedFrame", CaptureBitmap(reader.Current.Image, fixture.Id + "-reopened", outputDirectory, test));
                    reader.Close();
                    test.Equal(false, reader.Loaded, "loaded after final close");
                }
            }
        }

        private static void CharacterizeManualRotation(FixtureSpec fixture, string outputDirectory, ImageRotation rotation, CaseRecorder test)
        {
            var reader = new VideoReaderBitmap();
            test.Equal(OpenVideoResult.Success.ToString(), reader.Open(fixture.Path).ToString(), "open before manual rotation " + rotation);
            reader.MoveRequest(true, 0);
            var request = new VideoGeometryRequest(
                new Size(fixture.Width, fixture.Height), 1.0, 1.0, false,
                ImageAspectRatio.Auto, rotation, Demosaicing.None, false, null);
            bool updated = reader.UpdateVideoGeometry(request);
            test.Equal(false, updated, "geometry update return value " + rotation);
            test.Observation("geometry-" + rotation, new Dictionary<string, object>
            {
                { "referenceSize", reader.Geometry.ReferenceSize.Width + "x" + reader.Geometry.ReferenceSize.Height },
                { "outputSize", reader.Geometry.OutputSize.Width + "x" + reader.Geometry.OutputSize.Height },
                { "imageRotation", reader.Geometry.ImageRotation.ToString() },
                { "preScaled", reader.Geometry.IsPreScaled }
            });
            test.Equal(true, reader.MoveRequest(false, 1), "re-decode after manual rotation " + rotation);

            int turns = (int)rotation;
            int expectedWidth = turns == 1 || turns == 3 ? fixture.Height : fixture.Width;
            int expectedHeight = turns == 1 || turns == 3 ? fixture.Width : fixture.Height;
            test.Equal(expectedWidth + "x" + expectedHeight,
                reader.Current.Image.Width + "x" + reader.Current.Image.Height, "manual rotation dimensions " + rotation);
            test.Equal(rotation.ToString(), reader.Info.OriginalRotation.ToString(), "published original rotation " + rotation);
            test.Observation("rotation-" + rotation, CaptureBitmap(reader.Current.Image, "manual-" + rotation, outputDirectory, test));
            reader.Close();
        }

        private static void CharacterizeSummary(FixtureSpec fixture, string outputDirectory, CaseRecorder test)
        {
            var reader = new VideoReaderBitmap();
            VideoSummary summary = reader.ExtractSummary(fixture.Path, 1, new Size(4, 4));
            test.Equal("7x5", summary.ImageSize.Width + "x" + summary.ImageSize.Height, "summary source dimensions");
            test.Equal(true, summary.IsImage, "summary image flag");
            test.Equal(0L, summary.DurationMilliseconds, "summary duration milliseconds");
            test.Equal(1, summary.Thumbs.Count, "summary thumbnail count");
            if (summary.Thumbs.Count > 0)
            {
                test.Equal("4x2", summary.Thumbs[0].Width + "x" + summary.Thumbs[0].Height, "summary thumbnail dimensions");
                test.Observation("thumbnail", CaptureBitmap(summary.Thumbs[0], "summary-thumbnail", outputDirectory, test));
                foreach (SystemBitmap thumbnail in summary.Thumbs) thumbnail.Dispose();
            }
            test.Equal(false, reader.Loaded, "reader loaded state after summary extraction");
        }

        private static void CharacterizeCorruptInput(FixtureSpec fixture, CaseRecorder test)
        {
            var reader = new VideoReaderBitmap();
            {
                OpenVideoResult result = reader.Open(fixture.Path);
                test.Equal(OpenVideoResult.NotSupported.ToString(), result.ToString(), "corrupt PNG open result");
                test.Equal(false, reader.Loaded, "corrupt PNG loaded state");
                test.Observation("inputSha256", fixture.Sha256);
            }
        }

        private static void CharacterizeUnsupportedExtension(FixtureSpec fixture, CaseRecorder test)
        {
            test.Observation("inputSha256", fixture.Sha256);
            var reader = new VideoReaderBitmap();
            {
                bool threw = false;
                try { reader.Open(fixture.Path); }
                catch (NotImplementedException) { threw = true; }
                test.Equal(true, threw, "unsupported GIF extension throws NotImplementedException");
            }
        }

        private static Dictionary<string, object> CaptureBitmap(SystemBitmap bitmap, string id, string outputDirectory, CaseRecorder test)
        {
            Dictionary<string, object> capture = EvidenceWriter.CaptureBitmap(bitmap, id, outputDirectory);
            test.Equal<object>(null, capture["nativeCaptureError"], id + " native stride/pixel capture succeeds");
            object stride = capture["strideBytes"];
            test.True(stride != null && Math.Abs(Convert.ToInt32(stride, CultureInfo.InvariantCulture)) > 0,
                id + " records nonzero native stride");
            return capture;
        }

        private sealed class CaseRecorder
        {
            private readonly string name;
            private readonly List<Dictionary<string, object>> checks = new List<Dictionary<string, object>>();
            private readonly Dictionary<string, object> observations = new Dictionary<string, object>();
            public string Error;

            public CaseRecorder(string name) { this.name = name; }

            public void Equal<T>(T expected, T actual, string assertion)
            {
                bool passed = Object.Equals(expected, actual);
                AddCheck(assertion, expected, actual, passed);
            }

            public void True(bool actual, string assertion) { Equal(true, actual, assertion); }

            public void Observation(string key, object value) { observations[key] = value; }

            public void AddCheck(string assertion, object expected, object actual, bool passed)
            {
                assertionCount++;
                if (passed) passedAssertions++;
                var check = new Dictionary<string, object>
                {
                    { "name", assertion },
                    { "status", passed ? "PASS" : "FAIL" },
                    { "expected", JsonScalar(expected) },
                    { "actual", JsonScalar(actual) }
                };
                checks.Add(check);
                if (!passed)
                    Failures.Add(new Dictionary<string, object>
                    {
                        { "case", name }, { "assertion", assertion },
                        { "expected", JsonScalar(expected) }, { "actual", JsonScalar(actual) }
                    });
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    { "name", name },
                    { "status", Error == null && checks.All(c => (string)c["status"] == "PASS") ? "PASS" : "FAIL" },
                    { "error", Error },
                    { "assertionCount", checks.Count },
                    { "observations", observations },
                    { "assertions", checks }
                };
            }

            private static object JsonScalar(object value)
            {
                if (value == null || value is string || value is bool || value is int || value is long || value is double || value is float)
                    return value;
                return value.ToString();
            }
        }
    }
}
