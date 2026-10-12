using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
        private static readonly List<Dictionary<string, object>> StateTraces = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> SingleFrameTraces = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> ProductionCacheTraces = new List<Dictionary<string, object>>();
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
                RunCase("legacy-single-frame-cache-ownership", delegate(CaseRecorder test)
                {
                    CharacterizeSingleFrameOwnership(fixtures.First(f => f.Id == "bmp-rgb-7x5"), test);
                });
                RunCase("production-cache-contracts", CharacterizeProductionCacheContracts);
                RunCase("reader-state-trace", delegate(CaseRecorder test)
                {
                    CharacterizeReaderStateTrace(fixtures, test);
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
                    EvidenceWriter.WriteJson(Path.Combine(outputDirectory, "reader-state-traces.v1.json"), new Dictionary<string, object>
                    {
                        { "schemaVersion", 1 },
                        { "sourceReader", "Kinovea.Video.Bitmap.VideoReaderBitmap" },
                        { "fixtureGenerator", "FixtureFactory v1" },
                        { "approvalStatus", "CANDIDATE_NOT_APPROVED" },
                        { "sequences", StateTraces }
                    });
                    EvidenceWriter.WriteJson(Path.Combine(outputDirectory, "single-frame-cache-traces.v1.json"), new Dictionary<string, object>
                    {
                        { "schemaVersion", 1 },
                        { "sourceType", "Kinovea.Video.SingleFrame" },
                        { "approvalStatus", "CANDIDATE_NOT_APPROVED" },
                        { "sequences", SingleFrameTraces }
                    });
                    EvidenceWriter.WriteJson(Path.Combine(outputDirectory, "production-cache-traces.v1.json"), new Dictionary<string, object>
                    {
                        { "schemaVersion", 1 },
                        { "sourceTypes", new[] { "Kinovea.Video.Cache", "Kinovea.Video.PreBuffer2" } },
                        { "approvalStatus", "CANDIDATE_NOT_APPROVED" },
                        { "usageClassification", "direct-production-cache-contract-characterization; not full FFmpeg playback" },
                        { "sequences", ProductionCacheTraces }
                    });
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

        private static void CharacterizeSingleFrameOwnership(FixtureSpec fixture, CaseRecorder test)
        {
            // These are isolated contract probes: repository search found no production
            // construction of SingleFrame outside this characterization harness.
            var frames = new List<VideoFrame>();
            var images = new List<SystemBitmap>();
            var steps = new List<Dictionary<string, object>>();

            var firstGenerator = new FrameGeneratorImageFile();
            test.Equal(OpenVideoResult.Success.ToString(), firstGenerator.Open(fixture.Path).ToString(), "open first image generator");
            SystemBitmap firstImage = firstGenerator.Generate(0);
            var first = new VideoFrame(firstImage, 0, -1);
            var cache = new SingleFrame();
            CacheAddResult inserted = cache.Add(first);
            test.Equal(CacheAddResult.Added.ToString(), inserted.ToString(), "first insertion result");
            test.Equal(true, Object.ReferenceEquals(first, cache.CurrentFrame), "insertion preserves frame wrapper identity");
            test.Equal(false, IsDisposed(firstImage), "first inserted image remains usable");
            test.Equal(true, Object.ReferenceEquals(cache.CurrentFrame, cache.CurrentFrame), "repeated current access preserves identity");
            RecordSingleFrameStep(steps, "add-first", inserted.ToString(), cache, frames, images);
            RecordSingleFrameStep(steps, "read-current-repeated", "same-reference", cache, frames, images);

            var secondGenerator = new FrameGeneratorImageFile();
            test.Equal(OpenVideoResult.Success.ToString(), secondGenerator.Open(fixture.Path).ToString(), "open replacement image generator");
            SystemBitmap secondImage = secondGenerator.Generate(0);
            var second = new VideoFrame(secondImage, 1, 0);
            CacheAddResult replaced = cache.Add(second);
            test.Equal(CacheAddResult.Added.ToString(), replaced.ToString(), "replacement insertion result");
            test.Equal(true, Object.ReferenceEquals(second, cache.CurrentFrame), "replacement publishes new wrapper");
            test.Equal(true, IsDisposed(firstImage), "replacement Clear disposes old cached image");
            test.Equal(false, IsDisposed(secondImage), "replacement image remains usable");
            RecordSingleFrameStep(steps, "replace-with-distinct-image", replaced.ToString(), cache, frames, images);

            VideoFrame externalReference = cache.CurrentFrame;
            cache.Clear();
            test.Equal(true, cache.IsEmpty, "Clear empties cache");
            test.Equal(true, IsDisposed(externalReference.Image), "external image reference is unusable after default Clear");
            RecordSingleFrameStep(steps, "clear-with-external-reference", "cleared", cache, frames, images, externalReference);
            cache.Dispose();
            cache.Dispose();
            test.Equal(true, cache.IsEmpty, "repeated Dispose remains empty");
            RecordSingleFrameStep(steps, "dispose-repeated", "completed", cache, frames, images);
            firstGenerator.Close();
            secondGenerator.Close();

            var customGenerator = new FrameGeneratorImageFile();
            customGenerator.Open(fixture.Path);
            SystemBitmap customImage = customGenerator.Generate(0);
            var customFrame = new VideoFrame(customImage, 0, -1);
            int disposerCalls = 0;
            VideoFrame disposedByCallback = null;
            var customCache = new SingleFrame(delegate(VideoFrame frame)
            {
                disposerCalls++;
                disposedByCallback = frame;
            });
            customCache.Add(customFrame);
            RecordSingleFrameStep(steps, "custom-disposer-add", "Added", customCache, frames, images);
            customCache.Clear();
            test.Equal(1, disposerCalls, "custom disposer called once on Clear");
            test.Equal(true, Object.ReferenceEquals(customFrame, disposedByCallback), "custom disposer receives exact cached wrapper");
            test.Equal(false, IsDisposed(customImage), "custom no-op disposer leaves generator image usable");
            RecordSingleFrameStep(steps, "custom-disposer-clear", "callback-called", customCache, frames, images, customFrame);
            customCache.Dispose();
            customGenerator.Close();

            var reuseGenerator = new FrameGeneratorImageFile();
            reuseGenerator.Open(fixture.Path);
            SystemBitmap reusedImage = reuseGenerator.Generate(0);
            var reusedCache = new SingleFrame();
            var atZero = new VideoFrame(reusedImage, 0, -1);
            reusedCache.Add(atZero);
            SystemBitmap sameGeneratedImage = reuseGenerator.Generate(1);
            test.Equal(true, Object.ReferenceEquals(reusedImage, sameGeneratedImage), "generator reuses one bitmap across timestamps");
            var atOne = new VideoFrame(sameGeneratedImage, 1, 0);
            reusedCache.Add(atOne);
            test.Equal(true, Object.ReferenceEquals(atOne, reusedCache.CurrentFrame), "cache replacement stores second wrapper");
            test.Equal(true, IsDisposed(reusedImage), "default replacement disposes reused generator-owned bitmap");
            test.Equal(true, IsDisposed(reusedCache.CurrentFrame.Image), "replacement wrapper now points to disposed shared bitmap");
            RecordSingleFrameStep(steps, "replace-reused-generator-image", "same-image-disposed-by-eviction", reusedCache, frames, images);
            reuseGenerator.Close();
            reusedCache.Dispose();

            var reopenGenerator = new FrameGeneratorImageFile();
            reopenGenerator.Open(fixture.Path);
            SystemBitmap beforeClose = reopenGenerator.Generate(0);
            var reopenFrame = new VideoFrame(beforeClose, 0, -1);
            var reopenCache = new SingleFrame();
            reopenCache.Add(reopenFrame);
            reopenGenerator.Close();
            test.Equal(true, Object.ReferenceEquals(reopenFrame, reopenCache.CurrentFrame), "generator close leaves cached wrapper installed");
            test.Equal(true, IsDisposed(beforeClose), "generator close disposes cached image");
            RecordSingleFrameStep(steps, "generator-close-with-cached-frame", "cached-wrapper-stale", reopenCache, frames, images);
            reopenGenerator.Open(fixture.Path);
            SystemBitmap afterReopen = reopenGenerator.Generate(0);
            test.Equal(false, Object.ReferenceEquals(beforeClose, afterReopen), "generator reopen creates a different image");
            test.Equal(true, Object.ReferenceEquals(reopenFrame, reopenCache.CurrentFrame), "reopen does not update cache wrapper");
            var afterReopenFrame = new VideoFrame(afterReopen, 1, 0);
            reopenCache.Add(afterReopenFrame);
            test.Equal(true, Object.ReferenceEquals(afterReopenFrame, reopenCache.CurrentFrame), "replacement after reopen stores fresh wrapper");
            test.Equal(false, IsDisposed(afterReopen), "fresh reopened image remains usable after stale eviction");
            RecordSingleFrameStep(steps, "replace-stale-frame-after-reopen", "fresh-image-installed", reopenCache, frames, images);
            reopenCache.Clear();
            reopenGenerator.Close();
            reopenCache.Dispose();

            var disposeGenerator = new FrameGeneratorImageFile();
            disposeGenerator.Open(fixture.Path);
            SystemBitmap disposeImage = disposeGenerator.Generate(0);
            var disposeCache = new SingleFrame();
            VideoFrame disposedByCache = new VideoFrame(disposeImage, 0, -1);
            disposeCache.Add(disposedByCache);
            disposeCache.Dispose();
            test.Equal(true, disposeCache.IsEmpty, "Dispose clears a populated SingleFrame");
            test.Equal(true, IsDisposed(disposeImage), "Dispose releases the populated frame image by default");
            RecordSingleFrameStep(steps, "dispose-populated-slot", "disposed", disposeCache, frames, images, disposedByCache);
            disposeCache.Dispose();
            test.Equal(true, disposeCache.IsEmpty, "repeated Dispose on an empty SingleFrame remains empty");
            RecordSingleFrameStep(steps, "dispose-repeat", "completed", disposeCache, frames, images, disposedByCache);
            disposeGenerator.Close();

            SingleFrameTraces.Add(new Dictionary<string, object>
            {
                { "sequenceId", "single-frame-ownership" },
                { "usageClassification", "isolated-contract-probe-no-production-construction-callsite-found" },
                { "steps", steps }
            });
            test.Equal(12, steps.Count, "SingleFrame trace step count");
            test.Observation("singleFrameProductionCallSites", "none found outside characterization harness and type declaration");
            test.Observation("singleFrameTraceSchemaVersion", 1);
        }

        private static void CharacterizeProductionCacheContracts(CaseRecorder test)
        {
            CharacterizeSynchronousCache(test);
            CharacterizePreBuffer(test);
            test.Observation("scope", "Executes Kinovea.Video Cache and PreBuffer2 directly; does not instantiate VideoReaderFFMpeg or GIF reader.");
            test.Observation("productionCallSites", "VideoReaderGIF owns Cache; VideoReaderFFMpeg owns SingleFrame, PreBuffer2 and Cache.");
        }

        private static void CharacterizeSynchronousCache(CaseRecorder test)
        {
            var images = new List<SystemBitmap>();
            var frames = new List<VideoFrame>();
            var disposedTimestamps = new List<long>();
            var steps = new List<Dictionary<string, object>>();
            var cache = new Cache(delegate(VideoFrame frame)
            {
                disposedTimestamps.Add(frame.Timestamp);
                frame.Image.Dispose();
            });

            for (int i = 0; i < 3; i++)
            {
                var image = new SystemBitmap(2, 2);
                images.Add(image);
                var frame = new VideoFrame(image, i, i - 1);
                frames.Add(frame);
                test.Equal(CacheAddResult.Added.ToString(), cache.Add(frame).ToString(), "Cache add frame " + i);
            }

            cache.AcquireClosest(1);
            test.Equal(true, Object.ReferenceEquals(frames[1], cache.CurrentFrame), "Cache acquire preserves selected wrapper identity");
            AddCacheTrace(steps, "acquire-closest", cache.CurrentFrame, frames, disposedTimestamps);

            cache.Trim(1, 1);
            test.Equal("2,0", String.Join(",", disposedTimestamps), "Cache Trim dispatches custom disposer for evicted frames");
            test.Equal(true, Object.ReferenceEquals(frames[1], cache.CurrentFrame), "Cache Trim retains current frame in range");
            test.Equal(true, IsDisposed(images[0]) && IsDisposed(images[2]), "Trimmed external image references are disposed");
            AddCacheTrace(steps, "trim-to-current", cache.CurrentFrame, frames, disposedTimestamps);

            cache.Clear();
            test.Equal("2,0,1", String.Join(",", disposedTimestamps), "Cache Clear dispatches disposer for remaining frame");
            test.Equal(null, cache.CurrentFrame, "Cache Clear removes current frame");
            AddCacheTrace(steps, "clear", cache.CurrentFrame, frames, disposedTimestamps);

            cache.Dispose();
            cache.Dispose();
            test.Equal("2,0,1", String.Join(",", disposedTimestamps), "repeated Cache Dispose does not re-dispose frames");
            ProductionCacheTraces.Add(new Dictionary<string, object>
            {
                { "sequenceId", "cache-trim-clear-custom-disposer" },
                { "cacheType", "Kinovea.Video.Cache" },
                { "calls", new[] { "Add(0)", "Add(1)", "Add(2)", "AcquireClosest(1)", "Trim(1,1)", "Clear()", "Dispose()", "Dispose()" } },
                { "steps", steps }
            });
        }

        private static void CharacterizePreBuffer(CaseRecorder test)
        {
            var images = new List<SystemBitmap>();
            var frames = new List<VideoFrame>();
            var disposedTimestamps = new List<long>();
            var steps = new List<Dictionary<string, object>>();
            var buffer = new PreBuffer2(delegate(VideoFrame frame)
            {
                disposedTimestamps.Add(frame.Timestamp);
                frame.Image.Dispose();
            });

            for (int i = 0; i < 3; i++)
            {
                var image = new SystemBitmap(2, 2);
                images.Add(image);
                var frame = new VideoFrame(image, i * 10, (i - 1) * 10);
                frames.Add(frame);
                test.Equal(CacheAddResult.Added.ToString(), buffer.Add(frame).ToString(), "PreBuffer2 add frame " + i);
            }

            buffer.AcquireClosest(10);
            test.Equal(true, Object.ReferenceEquals(frames[1], buffer.CurrentFrame), "PreBuffer2 acquire closest selects current wrapper");
            test.Equal(true, buffer.AcquireNext(), "PreBuffer2 acquire next succeeds");
            test.Equal(true, Object.ReferenceEquals(frames[2], buffer.CurrentFrame), "PreBuffer2 acquire next selects contiguous frame");
            AddPreBufferTrace(steps, "acquire-next", buffer, frames, disposedTimestamps);

            buffer.Purge();
            test.Equal("10,0", String.Join(",", disposedTimestamps), "PreBuffer2 Purge disposes all noncurrent frames");
            test.Equal(true, Object.ReferenceEquals(frames[2], buffer.CurrentFrame), "PreBuffer2 Purge retains current frame");
            test.Equal(true, IsDisposed(images[0]) && IsDisposed(images[1]), "Purged external image references are disposed");
            AddPreBufferTrace(steps, "purge", buffer, frames, disposedTimestamps);

            buffer.Shutdown();
            test.Equal("10,0,20", String.Join(",", disposedTimestamps), "PreBuffer2 Shutdown disposes current frame");
            test.Equal(null, buffer.CurrentFrame, "PreBuffer2 Shutdown clears current frame");
            var rejected = new VideoFrame(new SystemBitmap(2, 2), 30, 20);
            CacheAddResult afterShutdown = buffer.Add(rejected);
            test.Equal(CacheAddResult.Interrupted.ToString(), afterShutdown.ToString(), "PreBuffer2 rejects add after Shutdown");
            bool rejectedImageUsable = !IsDisposed(rejected.Image);
            test.Equal(true, rejectedImageUsable, "caller retains rejected frame image after interrupted Add");
            rejected.Image.Dispose();
            buffer.Shutdown();
            ProductionCacheTraces.Add(new Dictionary<string, object>
            {
                { "sequenceId", "prebuffer-add-acquire-purge-shutdown" },
                { "cacheType", "Kinovea.Video.PreBuffer2" },
                { "calls", new[] { "Add(0)", "Add(10)", "Add(20)", "AcquireClosest(10)", "AcquireNext()", "Purge()", "Shutdown()", "Add(30)" } },
                { "steps", steps },
                { "disposerTimestamps", disposedTimestamps.ToArray() },
                { "postShutdownAddResult", afterShutdown.ToString() },
                { "postShutdownRejectedImageUsableBeforeCallerCleanup", rejectedImageUsable }
            });

            CharacterizePreBufferForceAddEviction(test);
        }

        private static void CharacterizePreBufferForceAddEviction(CaseRecorder test)
        {
            var images = new List<SystemBitmap>();
            var frames = new List<VideoFrame>();
            var disposedTimestamps = new List<long>();
            var buffer = new PreBuffer2(delegate(VideoFrame frame)
            {
                disposedTimestamps.Add(frame.Timestamp);
                frame.Image.Dispose();
            });

            for (int i = 0; i < 32; i++)
            {
                SystemBitmap image = new SystemBitmap(1, 1);
                images.Add(image);
                VideoFrame frame = new VideoFrame(image, i, i - 1);
                frames.Add(frame);
                test.Equal(CacheAddResult.Added.ToString(), buffer.Add(frame).ToString(), "PreBuffer2 fill capacity at " + i);
            }
            buffer.AcquireClosest(16);
            for (int i = 32; i < 40; i++)
            {
                SystemBitmap image = new SystemBitmap(1, 1);
                images.Add(image);
                VideoFrame frame = new VideoFrame(image, i, i - 1);
                frames.Add(frame);
                test.Equal(CacheAddResult.Added.ToString(), buffer.Add(frame).ToString(), "PreBuffer2 refill capacity at " + i);
            }

            VideoFrame evictedCandidate = frames[8];
            SystemBitmap forcedImage = new SystemBitmap(1, 1);
            images.Add(forcedImage);
            CacheAddResult forced = buffer.ForceAdd(new VideoFrame(forcedImage, 40, 39));
            bool callbackObservedForEvictedFrame = disposedTimestamps.Contains(evictedCandidate.Timestamp);
            bool evictedImageUsableBeforeShutdown = !IsDisposed(evictedCandidate.Image);
            int currentIdentityBeforeShutdown = IdentityOf(frames, buffer.CurrentFrame);
            long? currentTimestampBeforeShutdown = buffer.CurrentFrame == null ? (long?)null : buffer.CurrentFrame.Timestamp;
            test.Equal(CacheAddResult.Added.ToString(), forced.ToString(), "PreBuffer2 ForceAdd at capacity succeeds");
            test.Equal(32, buffer.Count, "PreBuffer2 ForceAdd maintains capacity");
            test.Observation("forceAddEvictedFrameTimestamp", evictedCandidate.Timestamp);
            test.Observation("forceAddEvictedFrameDisposerInvoked", callbackObservedForEvictedFrame);
            test.Observation("forceAddEvictedImageUsableBeforeShutdown", evictedImageUsableBeforeShutdown);
            test.Observation("forceAddCurrentFrameIdentity", currentIdentityBeforeShutdown);

            buffer.Shutdown();
            foreach (SystemBitmap image in images)
                if (!IsDisposed(image)) image.Dispose();
            ProductionCacheTraces.Add(new Dictionary<string, object>
            {
                { "sequenceId", "prebuffer-forceadd-at-capacity" },
                { "cacheType", "Kinovea.Video.PreBuffer2" },
                { "capacity", 32 },
                { "forceAddResult", forced.ToString() },
                { "currentFrameIdentityAfterForceAdd", currentIdentityBeforeShutdown },
                { "currentTimestampAfterForceAdd", currentTimestampBeforeShutdown },
                { "callingThreadId", Thread.CurrentThread.ManagedThreadId },
                { "evictedFrameTimestamp", evictedCandidate.Timestamp },
                { "evictedFrameDisposerInvokedBeforeShutdown", callbackObservedForEvictedFrame },
                { "evictedFrameImageUsableBeforeShutdown", evictedImageUsableBeforeShutdown },
                { "notes", "Observational characterization only; does not assert that a missing disposer callback is desired behavior." }
            });
        }

        private static void AddCacheTrace(List<Dictionary<string, object>> steps, string operation, VideoFrame current,
            List<VideoFrame> identities, List<long> disposed)
        {
            steps.Add(new Dictionary<string, object>
            {
                { "operation", operation }, { "currentTimestamp", current == null ? (object)null : current.Timestamp },
                { "currentFrameIdentity", current == null ? 0 : IdentityOf(identities, current) },
                { "disposedTimestamps", disposed.ToArray() },
                { "currentImageUsable", current == null ? (object)null : !IsDisposed(current.Image) },
                { "callingThreadId", Thread.CurrentThread.ManagedThreadId }
            });
        }

        private static void AddPreBufferTrace(List<Dictionary<string, object>> steps, string operation, PreBuffer2 buffer,
            List<VideoFrame> identities, List<long> disposed)
        {
            VideoFrame current = buffer.CurrentFrame;
            steps.Add(new Dictionary<string, object>
            {
                { "operation", operation }, { "count", buffer.Count },
                { "currentTimestamp", current == null ? (object)null : current.Timestamp },
                { "currentFrameIdentity", current == null ? 0 : IdentityOf(identities, current) },
                { "currentPreviousTimestamp", current == null ? (object)null : current.PreviousTimestamp },
                { "disposedTimestamps", disposed.ToArray() },
                { "currentImageUsable", current == null ? (object)null : !IsDisposed(current.Image) },
                { "callingThreadId", Thread.CurrentThread.ManagedThreadId }
            });
        }

        private static void RecordSingleFrameStep(List<Dictionary<string, object>> steps, string operation, string result,
            SingleFrame cache, List<VideoFrame> frames, List<SystemBitmap> images, VideoFrame observedExternalFrame = null)
        {
            VideoFrame current = cache.CurrentFrame;
            int frameIdentity = IdentityId(frames, current);
            int imageIdentity = current == null ? 0 : IdentityId(images, current.Image);
            int externalFrameIdentity = IdentityId(frames, observedExternalFrame);
            int externalImageIdentity = observedExternalFrame == null ? 0 : IdentityId(images, observedExternalFrame.Image);
            string pixelAccess = "empty";
            if (current != null && current.Image != null)
            {
                try
                {
                    Color pixel = current.Image.GetPixel(0, 0);
                    pixelAccess = String.Format(CultureInfo.InvariantCulture, "usable:{0},{1},{2},{3}", pixel.R, pixel.G, pixel.B, pixel.A);
                }
                catch (Exception ex)
                {
                    pixelAccess = "error:" + ex.GetType().FullName;
                }
            }
            string externalPixelAccess = "not-observed";
            if (observedExternalFrame != null && observedExternalFrame.Image != null)
            {
                try
                {
                    Color pixel = observedExternalFrame.Image.GetPixel(0, 0);
                    externalPixelAccess = String.Format(CultureInfo.InvariantCulture, "usable:{0},{1},{2},{3}", pixel.R, pixel.G, pixel.B, pixel.A);
                }
                catch (Exception ex)
                {
                    externalPixelAccess = "error:" + ex.GetType().FullName;
                }
            }

            steps.Add(new Dictionary<string, object>
            {
                { "operation", operation },
                { "result", result },
                { "currentFrameIdentity", frameIdentity },
                { "currentImageIdentity", imageIdentity },
                { "observedExternalFrameIdentity", externalFrameIdentity },
                { "observedExternalImageIdentity", externalImageIdentity },
                { "timestamp", current == null ? (object)null : current.Timestamp },
                { "previousTimestamp", current == null ? (object)null : current.PreviousTimestamp },
                { "pixelAccess", pixelAccess },
                { "externalPixelAccess", externalPixelAccess },
                { "isEmpty", cache.IsEmpty }
            });
        }

        private static int IdentityId<T>(List<T> identities, T value) where T : class
        {
            if (value == null)
                return 0;
            int index = identities.FindIndex(existing => Object.ReferenceEquals(existing, value));
            if (index >= 0)
                return index + 1;
            identities.Add(value);
            return identities.Count;
        }

        private static bool IsDisposed(SystemBitmap bitmap)
        {
            try { bitmap.GetPixel(0, 0); return false; }
            catch (ArgumentException) { return true; }
        }

        private static void CharacterizeReaderStateTrace(List<FixtureSpec> fixtures, CaseRecorder test)
        {
            FixtureSpec bmp = fixtures.First(f => f.Id == "bmp-rgb-7x5");
            FixtureSpec corrupt = fixtures.First(f => f.Format == "INVALID");
            var frameIds = new List<VideoFrame>();
            var imageIds = new List<SystemBitmap>();
            var steps = new List<Dictionary<string, object>>();
            var reader = new VideoReaderBitmap();

            TraceStep(steps, frameIds, imageIds, "construct", new Dictionary<string, object>(), reader, delegate { return "completed"; });
            TraceStep(steps, frameIds, imageIds, "move-next-unopened", new Dictionary<string, object> { { "next", true }, { "target", 321L } }, reader, delegate { return reader.MoveRequest(true, 321); });
            TraceStep(steps, frameIds, imageIds, "open-bmp", new Dictionary<string, object> { { "fixtureId", bmp.Id } }, reader, delegate { return reader.Open(bmp.Path).ToString(); });
            TraceStep(steps, frameIds, imageIds, "move-next-first", new Dictionary<string, object> { { "next", true }, { "target", 321L } }, reader, delegate { return reader.MoveRequest(true, 321); });
            TraceStep(steps, frameIds, imageIds, "move-next-second", new Dictionary<string, object> { { "next", true }, { "target", -900L } }, reader, delegate { return reader.MoveRequest(true, -900); });
            TraceStep(steps, frameIds, imageIds, "repeat-current", new Dictionary<string, object> { { "next", false }, { "target", 1L } }, reader, delegate { return reader.MoveRequest(false, 1); });
            TraceStep(steps, frameIds, imageIds, "seek-forward", new Dictionary<string, object> { { "target", 37L } }, reader, delegate { return reader.MoveRequest(false, 37); });
            TraceStep(steps, frameIds, imageIds, "seek-backward", new Dictionary<string, object> { { "target", 12L } }, reader, delegate { return reader.MoveRequest(false, 12); });
            TraceStep(steps, frameIds, imageIds, "seek-last-valid", new Dictionary<string, object> { { "target", 249L } }, reader, delegate { return reader.MoveRequest(false, 249); });
            TraceStep(steps, frameIds, imageIds, "next-past-end", new Dictionary<string, object> { { "next", true }, { "target", 0L } }, reader, delegate { return reader.MoveRequest(true, 0); });
            TraceStep(steps, frameIds, imageIds, "seek-outside-zone", new Dictionary<string, object> { { "target", 250L } }, reader, delegate { return reader.MoveRequest(false, 250); });
            TraceStep(steps, frameIds, imageIds, "player-timestamp", new Dictionary<string, object> { { "action", PlayerAction.Timestamp.ToString() }, { "reference", 9L } }, reader, delegate { return reader.PlayerRequest(new PlayerState(40, PlayerAction.Timestamp, true, 9)); });
            TraceStep(steps, frameIds, imageIds, "player-step-forward", new Dictionary<string, object> { { "action", PlayerAction.StepForward.ToString() }, { "reference", 249L } }, reader, delegate { return reader.PlayerRequest(new PlayerState(41, PlayerAction.StepForward, true, 249)); });
            TraceStep(steps, frameIds, imageIds, "player-step-backward", new Dictionary<string, object> { { "action", PlayerAction.StepBackward.ToString() }, { "reference", 0L } }, reader, delegate { return reader.PlayerRequest(new PlayerState(42, PlayerAction.StepBackward, true, 0)); });
            TraceStep(steps, frameIds, imageIds, "close", new Dictionary<string, object>(), reader, delegate { reader.Close(); return "completed"; });
            TraceStep(steps, frameIds, imageIds, "close-repeat", new Dictionary<string, object>(), reader, delegate { reader.Close(); return "completed"; });
            TraceStep(steps, frameIds, imageIds, "reopen-bmp", new Dictionary<string, object> { { "fixtureId", bmp.Id } }, reader, delegate { return reader.Open(bmp.Path).ToString(); });
            TraceStep(steps, frameIds, imageIds, "seek-same-after-reopen", new Dictionary<string, object> { { "target", 9L } }, reader, delegate { return reader.MoveRequest(false, 9); });
            TraceStep(steps, frameIds, imageIds, "seek-different-after-reopen", new Dictionary<string, object> { { "target", 8L } }, reader, delegate { return reader.MoveRequest(false, 8); });
            TraceStep(steps, frameIds, imageIds, "close-final", new Dictionary<string, object>(), reader, delegate { reader.Close(); return "completed"; });

            StateTraces.Add(new Dictionary<string, object> { { "sequenceId", "navigation-close-reopen" }, { "steps", steps } });
            test.Equal(20, steps.Count, "versioned navigation trace step count");
            test.Equal(0L, (long)StateOf(steps[3])["currentTimestamp"], "trace first frame timestamp");
            test.Equal(1L, (long)StateOf(steps[4])["currentTimestamp"], "trace second frame timestamp");
            test.Equal(true, Object.Equals(StateOf(steps[4])["frameIdentity"], StateOf(steps[5])["frameIdentity"]), "trace repeated timestamp frame identity");
            test.Equal(false, (bool)steps[9]["result"], "trace next past end return");
            test.Equal(249L, (long)StateOf(steps[10])["currentTimestamp"], "trace outside-zone unchanged timestamp");
            test.Equal(9L, (long)StateOf(steps[17])["currentTimestamp"], "trace close/reopen same timestamp retains current position");
            test.Equal(StateOf(steps[13])["frameIdentity"], StateOf(steps[17])["frameIdentity"], "trace same timestamp after reopen retains wrapper identity");
            test.Equal("System.ArgumentException", StateOf(steps[17])["pixelAccessErrorType"].ToString(), "trace same timestamp after reopen exposes disposed image failure");
            test.Equal(8L, (long)StateOf(steps[18])["currentTimestamp"], "trace different timestamp after reopen selects fresh frame");
            test.Observation("traceSchemaVersion", 1);

            var recoverySteps = new List<Dictionary<string, object>>();
            var recoveryFrames = new List<VideoFrame>();
            var recoveryImages = new List<SystemBitmap>();
            var recovery = new VideoReaderBitmap();
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "construct", new Dictionary<string, object>(), recovery, delegate { return "completed"; });
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "open-corrupt-png", new Dictionary<string, object> { { "fixtureId", corrupt.Id } }, recovery, delegate { return recovery.Open(corrupt.Path).ToString(); });
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "seek-zero-after-failure", new Dictionary<string, object> { { "target", 0L } }, recovery, delegate { return recovery.MoveRequest(false, 0); });
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "recover-open-bmp", new Dictionary<string, object> { { "fixtureId", bmp.Id } }, recovery, delegate { return recovery.Open(bmp.Path).ToString(); });
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "repeat-zero-after-recovery", new Dictionary<string, object> { { "target", 0L } }, recovery, delegate { return recovery.MoveRequest(false, 0); });
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "seek-one-after-recovery", new Dictionary<string, object> { { "target", 1L } }, recovery, delegate { return recovery.MoveRequest(false, 1); });
            TraceStep(recoverySteps, recoveryFrames, recoveryImages, "close", new Dictionary<string, object>(), recovery, delegate { recovery.Close(); return "completed"; });
            StateTraces.Add(new Dictionary<string, object> { { "sequenceId", "failed-open-recovery" }, { "steps", recoverySteps } });
            test.Equal("NotSupported", recoverySteps[1]["result"].ToString(), "corrupt PNG legacy open result");
            test.Equal(false, (bool)StateOf(recoverySteps[1])["loaded"], "failed open loaded state");
            test.Equal("Success", recoverySteps[3]["result"].ToString(), "valid image recovery result");
            test.Equal(0L, (long)StateOf(recoverySteps[4])["currentTimestamp"], "recovery repeat zero retains current frame");

            var openedFailureSteps = new List<Dictionary<string, object>>();
            var openedFailureFrames = new List<VideoFrame>();
            var openedFailureImages = new List<SystemBitmap>();
            var openedFailure = new VideoReaderBitmap();
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "construct", new Dictionary<string, object>(), openedFailure, delegate { return "completed"; });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "open-bmp", new Dictionary<string, object> { { "fixtureId", bmp.Id } }, openedFailure, delegate { return openedFailure.Open(bmp.Path).ToString(); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "first-frame", new Dictionary<string, object>(), openedFailure, delegate { return openedFailure.MoveRequest(true, 0); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "failed-open-after-valid", new Dictionary<string, object> { { "fixtureId", corrupt.Id } }, openedFailure, delegate { return openedFailure.Open(corrupt.Path).ToString(); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "repeat-current-after-failure", new Dictionary<string, object> { { "target", 0L } }, openedFailure, delegate { return openedFailure.MoveRequest(false, 0); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "recover-open-bmp", new Dictionary<string, object> { { "fixtureId", bmp.Id } }, openedFailure, delegate { return openedFailure.Open(bmp.Path).ToString(); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "repeat-current-after-recovery", new Dictionary<string, object> { { "target", 0L } }, openedFailure, delegate { return openedFailure.MoveRequest(false, 0); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "seek-one-after-recovery", new Dictionary<string, object> { { "target", 1L } }, openedFailure, delegate { return openedFailure.MoveRequest(false, 1); });
            TraceStep(openedFailureSteps, openedFailureFrames, openedFailureImages, "close", new Dictionary<string, object>(), openedFailure, delegate { openedFailure.Close(); return "completed"; });
            StateTraces.Add(new Dictionary<string, object> { { "sequenceId", "failed-open-after-valid" }, { "steps", openedFailureSteps } });
            test.Equal(9, openedFailureSteps.Count, "failed-open-after-valid trace operation count");
            test.Equal("NotSupported", openedFailureSteps[3]["result"].ToString(), "failed open after image result");
            test.Equal(false, (bool)StateOf(openedFailureSteps[3])["loaded"], "failed open after image loaded state");
            test.Equal(true, StateOf(openedFailureSteps[3])["hasImage"], "legacy retains current image reference after failed open");
            test.Equal(StateOf(openedFailureSteps[2])["imageIdentity"], StateOf(openedFailureSteps[3])["imageIdentity"], "legacy retains prior image identity after failed open");
            test.Equal(0L, (long)StateOf(openedFailureSteps[6])["currentTimestamp"], "same timestamp remains selected after recovery");
            test.Equal(StateOf(openedFailureSteps[2])["imageIdentity"], StateOf(openedFailureSteps[6])["imageIdentity"], "legacy keeps prior image identity after recovery at same timestamp");
            test.Equal(1L, (long)StateOf(openedFailureSteps[7])["currentTimestamp"], "different timestamp after recovery selects frame");

            var unopenedSteps = new List<Dictionary<string, object>>();
            var unopenedFrames = new List<VideoFrame>();
            var unopenedImages = new List<SystemBitmap>();
            var unopened = new VideoReaderBitmap();
            TraceStep(unopenedSteps, unopenedFrames, unopenedImages, "close-unopened", new Dictionary<string, object>(), unopened, delegate { unopened.Close(); return "completed"; });
            StateTraces.Add(new Dictionary<string, object> { { "sequenceId", "close-unopened" }, { "steps", unopenedSteps } });
            test.Equal("System.NullReferenceException", unopenedSteps[0]["errorType"].ToString(), "legacy close-unopened failure category");
        }

        private static void TraceStep(List<Dictionary<string, object>> steps, List<VideoFrame> frameIds, List<SystemBitmap> imageIds, string operation, object inputs, VideoReaderBitmap reader, Func<object> action)
        {
            object result = null;
            string errorType = null;
            try { result = action(); }
            catch (Exception ex) { errorType = ex.GetType().FullName; }
            object traceResult = result is Enum ? result.ToString() : result;
            VideoFrame current = reader.Current;
            SystemBitmap image = current == null ? null : current.Image;
            var state = new Dictionary<string, object>
            {
                { "loaded", reader.Loaded },
                { "currentTimestamp", current == null ? (object)null : current.Timestamp },
                { "previousTimestamp", current == null ? (object)null : current.PreviousTimestamp },
                { "hasImage", image != null },
                { "frameIdentity", current == null ? (object)null : IdentityOf(frameIds, current) },
                { "imageIdentity", image == null ? (object)null : IdentityOf(imageIds, image) },
                { "workingZoneStart", reader.WorkingZone.Start },
                { "workingZoneEnd", reader.WorkingZone.End },
                { "framesPerSecond", reader.Info.FramesPerSeconds },
                { "averageTimestampsPerFrame", reader.Info.AverageTimeStampsPerFrame },
                { "durationTimestamps", reader.Info.DurationTimeStamps },
                { "originalWidth", reader.Info.OriginalSize.Width },
                { "originalHeight", reader.Info.OriginalSize.Height },
                { "frameIntervalMilliseconds", reader.Info.FrameIntervalMilliseconds },
                { "firstTimestamp", reader.Info.FirstTimeStamp },
                { "lastTimestamp", reader.Info.LastTimeStamp }
            };
            if (image != null)
            {
                try
                {
                    Color c = image.GetPixel(0, 0);
                    state["pixelProbe"] = new Dictionary<string, object> { { "r", c.R }, { "g", c.G }, { "b", c.B }, { "a", c.A } };
                }
                catch (Exception ex) { state["pixelAccessErrorType"] = ex.GetType().FullName; }
            }
            steps.Add(new Dictionary<string, object>
            {
                { "operation", operation }, { "inputs", inputs }, { "result", traceResult },
                { "errorType", errorType }, { "stateAfter", state }
            });
        }

        private static int IdentityOf<T>(List<T> identities, T item) where T : class
        {
            for (int i = 0; i < identities.Count; i++) if (Object.ReferenceEquals(identities[i], item)) return i;
            identities.Add(item);
            return identities.Count - 1;
        }

        private static Dictionary<string, object> StateOf(Dictionary<string, object> step)
        {
            return (Dictionary<string, object>)step["stateAfter"];
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
