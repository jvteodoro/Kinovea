using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Kinovea.Video;
using Kinovea.Video.Bitmap;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            string fixtureRoot = Option(args, "--fixtures") ?? throw new ArgumentException("--fixtures is required.");
            string output = Option(args, "--output") ?? throw new ArgumentException("--output is required.");
            string sourceSha = Environment.GetEnvironmentVariable("KINOVEA_SOURCE_SHA") ?? "LOCAL_UNRECORDED";

            VideoTypeManager.LoadVideoReaders(new List<Type> { typeof(VideoReaderBitmap) });
            Check(true, VideoTypeManager.IsSupported(".bmp"), "VideoTypeManager registers .bmp");
            Check(true, VideoTypeManager.IsSupported(".png"), "VideoTypeManager registers .png");
            Check(false, VideoTypeManager.IsSupported(".gif"), "Bitmap-only manager does not register .gif");

            var cases = new List<object>
            {
                Exercise("bmp-rgb-7x5", Path.Combine(fixtureRoot, "bmp-rgb-7x5.bmp"),
                    expectedWidth: 7, expectedHeight: 5, expectedPixel: new[] { 17, 29, 43, 255 }),
                Exercise("png-alpha-13x9", Path.Combine(fixtureRoot, "png-alpha-13x9.png"),
                    expectedWidth: 13, expectedHeight: 9, expectedPixel: new[] { 54, 46, 134, 64 }),
                ExerciseFailedOpen(Path.Combine(fixtureRoot, "corrupt-image.png"))
            };

            VideoReader noReader = VideoTypeManager.GetVideoReader(".gif");
            Check(null, noReader, "unregistered extension has no reader");

            string trace = new JavaScriptSerializer().Serialize(new
            {
                schemaVersion = 1,
                characterization = "actual-VideoTypeManager-to-VideoReaderBitmap-open-current-close; no FrameServerPlayer or UI",
                sourceSha,
                os = Environment.OSVersion.ToString(),
                clrVersion = Environment.Version.ToString(),
                readerType = typeof(VideoReaderBitmap).FullName,
                cases,
                assertionCount = assertions
            });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, trace, new UTF8Encoding(false));
            Console.WriteLine("Legacy still-image selection characterization: PASS (" + assertions + " assertions)");
            Console.WriteLine("Trace: " + Path.GetFullPath(output));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Legacy still-image selection characterization: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static object Exercise(string id, string path, int expectedWidth, int expectedHeight, int[] expectedPixel)
    {
        VideoReader reader = VideoTypeManager.GetVideoReader(Path.GetExtension(path));
        Check(typeof(VideoReaderBitmap), reader.GetType(), id + " selected actual Bitmap reader");
        Check(OpenVideoResult.Success, reader.Open(path), id + " open result");
        Check(-1L, reader.Current.Timestamp, id + " initial current timestamp");
        Check(-1L, reader.Current.PreviousTimestamp, id + " initial previous timestamp");
        Check(25d, reader.Info.FramesPerSeconds, id + " legacy FPS");
        Check(1d, reader.Info.AverageTimeStampsPerFrame, id + " timestamps per frame");
        Check(250L, reader.Info.DurationTimeStamps, id + " legacy duration");

        Check(true, reader.MoveRequest(true, 731), id + " first sequential request");
        VideoFrame first = reader.Current;
        Check(0L, first.Timestamp, id + " first frame timestamp");
        Check(-1L, first.PreviousTimestamp, id + " first previous timestamp");
        Check(expectedWidth, first.Image.Width, id + " current width");
        Check(expectedHeight, first.Image.Height, id + " current height");
        int width = first.Image.Width;
        int height = first.Image.Height;
        Color pixel = first.Image.GetPixel(id.StartsWith("png", StringComparison.Ordinal) ? 1 : 0, 0);
        Check(expectedPixel[0], (int)pixel.R, id + " actual reader pixel red");
        Check(expectedPixel[1], (int)pixel.G, id + " actual reader pixel green");
        Check(expectedPixel[2], (int)pixel.B, id + " actual reader pixel blue");
        Check(expectedPixel[3], (int)pixel.A, id + " actual reader pixel alpha");
        Check(true, reader.MoveRequest(false, 0), id + " repeat-current request");
        bool sameFrame = Object.ReferenceEquals(first, reader.Current);
        bool sameImage = Object.ReferenceEquals(first.Image, reader.Current.Image);
        Check(true, sameFrame, id + " repeat preserves VideoFrame identity");
        Check(true, sameImage, id + " repeat preserves Bitmap identity");

        reader.Close();
        string disposedAccessException = null;
        try { _ = first.Image.GetPixel(0, 0); }
        catch (Exception ex) { disposedAccessException = ex.GetType().FullName; }
        Check(true, disposedAccessException != null, id + " close invalidates Bitmap access");

        return new
        {
            id,
            selectedReaderType = reader.GetType().FullName,
            openResult = OpenVideoResult.Success.ToString(),
            initialTimestamp = -1,
            initialPreviousTimestamp = -1,
            firstTimestamp = first.Timestamp,
            firstPreviousTimestamp = first.PreviousTimestamp,
            width,
            height,
            pixel = new { x = id.StartsWith("png", StringComparison.Ordinal) ? 1 : 0, y = 0, red = pixel.R, green = pixel.G, blue = pixel.B, alpha = pixel.A },
            repeatedVideoFrameIdentity = sameFrame,
            repeatedBitmapIdentity = sameImage,
            pixelAccessAfterCloseException = disposedAccessException
        };
    }

    private static object ExerciseFailedOpen(string path)
    {
        VideoReader reader = VideoTypeManager.GetVideoReader(Path.GetExtension(path));
        Check(typeof(VideoReaderBitmap), reader.GetType(), "corrupt PNG selected actual Bitmap reader");
        Check(OpenVideoResult.NotSupported, reader.Open(path), "corrupt PNG open status");
        Check(false, reader.Loaded, "corrupt PNG remains unloaded");
        reader.Close();
        return new
        {
            id = "corrupt-png",
            selectedReaderType = reader.GetType().FullName,
            openResult = OpenVideoResult.NotSupported.ToString(),
            loadedAfterOpen = reader.Loaded
        };
    }

    private static string Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Check<T>(T expected, T actual, string name)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected '" + expected + "', got '" + actual + "'.");
    }
}
