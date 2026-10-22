using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kinovea.PortableFrameGenerator;
using Kinovea.PortableDesktop.Presentation;
using Kinovea.PortableReaderShell;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            AppBuilder.Configure<TestApp>().UsePlatformDetect().SetupWithoutStarting();
            if (args.Length == 2 && args[0] == "--measure")
                return Measure(args[1]);

            TestViewportTransform();
            TestPixelAdapter();
            TestSessionLifecycle();
            TestOpenFailures();
            Console.WriteLine($"Portable desktop presentation/player tests: PASS ({assertions} assertions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Portable desktop presentation/player tests: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestViewportTransform()
    {
        var portrait = ViewportTransform.Create(276, 398, new Size(1000, 600));
        Near(600d / 398, portrait.Scale, 1e-12, "portrait fit scale");
        Near(1000, portrait.ImageRectangle.Width + (portrait.Offset.X * 2), 1e-9, "portrait centered horizontal offset");
        Point center = portrait.ImageToViewport(new Point(138, 199));
        Near(500, center.X, 1e-9, "portrait center x");
        Near(300, center.Y, 1e-9, "portrait center y");
        Point roundTrip = portrait.ViewportToImage(center);
        Near(138, roundTrip.X, 1e-10, "image to viewport x round trip");
        Near(199, roundTrip.Y, 1e-10, "image to viewport y round trip");

        var landscape = ViewportTransform.Create(1280, 720, new Size(900, 700));
        Near(900, landscape.ImageRectangle.Width, 1e-9, "landscape fits width");
        Near(506.25, landscape.ImageRectangle.Height, 1e-9, "landscape aspect ratio preserved");
        var resized = ViewportTransform.Create(1280, 720, new Size(500, 800));
        Near(500d / 1280, resized.Scale, 1e-12, "resized viewport fit scale");

        Point anchor = new(311.5, 217.25);
        Point imageAnchor = landscape.ViewportToImage(anchor);
        Vector zoomPan = ViewportTransform.PanForZoomAt(anchor, imageAnchor, new Size(900, 700), 1280, 720, 2.25);
        var zoomed = ViewportTransform.Create(1280, 720, new Size(900, 700), 2.25, zoomPan);
        Point unchangedAnchor = zoomed.ImageToViewport(imageAnchor);
        Near(anchor.X, unchangedAnchor.X, 1e-9, "zoom anchor x remains fixed");
        Near(anchor.Y, unchangedAnchor.Y, 1e-9, "zoom anchor y remains fixed");

        Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(0, 10, new Size(20, 20)), "invalid image dimensions rejected");
        Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size(0, 20)), "invalid viewport rejected");
        Throws<ArgumentOutOfRangeException>(() => ViewportTransform.Create(10, 10, new Size(20, 20), 0), "invalid zoom rejected");
    }

    private static void TestPixelAdapter()
    {
        string bmpPath = Fixture("bmp-rgb-7x5.bmp");
        using var reader = new PortableReaderShell();
        Equal(PortableOpenResult.Success, reader.Open(bmpPath), "open RGB BMP");
        True(reader.MoveRequest(true, -1), "get first BMP frame");
        PortableImageFrame frame = reader.Current.Image!;
        PixelBitmapResult result = PixelBufferBitmapAdapter.Create(frame);
        Equal(7, result.Width, "adapter width");
        Equal(5, result.Height, "adapter height");
        Equal(1, result.CopyCount, "adapter performs one full-frame copy");
        Equal(105L, result.BytesCopied, "adapter copied only logical RGB bytes");
        VerifyBitmapRows(result.Bitmap, frame);
        reader.Close();
        Equal((byte)17, ReadBitmapPixel(result.Bitmap, 0, 0, 3)[0], "owned presentation survives source pixel owner close");
        result.Bitmap.Dispose();

        string pngPath = Fixture("png-alpha-13x9.png");
        using var pngReader = new PortableReaderShell();
        Equal(PortableOpenResult.Success, pngReader.Open(pngPath), "open RGBA PNG");
        True(pngReader.MoveRequest(true, -1), "get first PNG frame");
        PortableImageFrame pngFrame = pngReader.Current.Image!;
        PixelBitmapResult png = PixelBufferBitmapAdapter.Create(pngFrame);
        Equal(AlphaFormat.Unpremul, png.Bitmap.AlphaFormat, "straight alpha label preserved");
        Equal(468L, png.BytesCopied, "RGBA copy byte count");
        VerifyBitmapRows(png.Bitmap, pngFrame);
        byte[] transparent = ReadBitmapPixel(png.Bitmap, 0, 0, 4);
        Equal((byte)0, transparent[3], "transparent alpha preserved");
        pngReader.Close();
        Equal((byte)0, ReadBitmapPixel(png.Bitmap, 0, 0, 4)[3], "presentation bytes survive source close");
        png.Bitmap.Dispose();
    }

    private static void TestSessionLifecycle()
    {
        using var session = new PortablePlayerSession();
        Equal(PortablePlayerStatus.Empty, session.Status, "initial empty player");
        Equal(-1L, session.Current.Timestamp, "initial timestamp");
        True(session.Open(Fixture("bmp-rgb-7x5.bmp")), "successful BMP open");
        Equal(PortablePlayerStatus.Open, session.Status, "opened player state");
        Equal(0L, session.Current.Timestamp, "initial frame timestamp");
        Equal(-1L, session.Current.PreviousTimestamp, "initial previous timestamp");
        Equal(25d, session.Metadata!.FramesPerSecond, "legacy fps metadata");
        Equal(0L, session.TimelineStart, "legacy timeline start");
        Equal(249L, session.TimelineEnd, "legacy timeline end");
        True(session.PresentationBitmap is not null, "presentation bitmap is available");
        True(!session.CanStepBackward, "previous disabled at first timestamp");
        True(session.CanStepForward, "next enabled at first timestamp");

        PortableReaderFrame first = session.Current;
        True(session.RepeatCurrent(), "repeat current preserves reader wrapper identity");
        Same(first, session.Current, "same wrapper remains current");
        True(session.StepForward(), "next frame step");
        Equal(1L, session.Current.Timestamp, "next timestamp");
        Equal(0L, session.Current.PreviousTimestamp, "next previous timestamp");
        True(MemoryMarshal.TryGetArray(first.Image!.Memory, out ArraySegment<byte> firstBytes), "first frame uses managed backing storage");
        True(MemoryMarshal.TryGetArray(session.Current.Image!.Memory, out ArraySegment<byte> nextBytes), "next frame uses managed backing storage");
        Same(firstBytes.Array, nextBytes.Array, "repeat/step reuses decoded pixel owner");
        True(session.StepBackward(), "previous frame step");
        Equal(0L, session.Current.Timestamp, "back step timestamp");
        True(session.Seek(249), "seek to last working-zone timestamp");
        Equal(249L, session.Current.Timestamp, "seek timestamp");
        True(!session.CanStepForward, "next disabled at working-zone boundary");
        True(!session.StepForward(), "out-of-zone next is rejected by player command");
        True(!session.Seek(250), "seek beyond working-zone boundary rejected");
        True(session.CanStepBackward, "previous enabled at end");

        WriteableBitmap oldPresentation = session.PresentationBitmap!;
        PortableImageFrame oldFrame = session.Current.Image!;
        session.Close();
        Equal(PortablePlayerStatus.Empty, session.Status, "close returns to empty state");
        True(session.PresentationBitmap is null, "close clears presentation reference");
        Equal(-1L, session.Current.Timestamp, "close clears current wrapper");
        Throws<ObjectDisposedException>(() => _ = oldFrame.Memory.Length, "close releases reader pixel memory");
        Throws<ObjectDisposedException>(() => oldPresentation.Lock(), "close disposes independent Avalonia bitmap");

        True(session.Open(Fixture("png-alpha-13x9.png")), "reopen different PNG");
        Equal(0L, session.Current.Timestamp, "reopen selects timestamp zero");
        Equal(-1L, session.Current.PreviousTimestamp, "reopen previous timestamp");
        Equal(13, session.Metadata!.Width, "reopen width");
        Equal(9, session.Metadata.Height, "reopen height");
        Equal(2L, session.PresentationCopies, "one presentation copy for each open interval");
        Equal(573L, session.PresentationBytesCopied, "cumulative copied bytes over both images");
        session.Dispose();
        session.Dispose();
        Equal(PortablePlayerStatus.Empty, session.Status, "repeated dispose closes resources");
        Throws<ObjectDisposedException>(() => session.Open(Fixture("bmp-rgb-7x5.bmp")), "open after dispose rejected");
    }

    private static void TestOpenFailures()
    {
        using var session = new PortablePlayerSession();
        True(!session.Open(Fixture("unsupported.gif")), "unsupported format rejected");
        Equal(PortablePlayerStatus.Failed, session.Status, "unsupported file status");
        True(session.PresentationBitmap is null, "unsupported file has no presentation bitmap");
        True(session.ErrorMessage is not null, "unsupported file exposes error text");
        True(!session.Open(Fixture("corrupt-image.png")), "corrupt PNG rejected");
        Equal(PortablePlayerStatus.Failed, session.Status, "corrupt file status");
        True(session.PresentationBitmap is null, "corrupt file has no presentation bitmap");
        session.Close();
        Equal(PortablePlayerStatus.Empty, session.Status, "close clears failure state");

        True(session.Open(Fixture("bmp-rgb-7x5.bmp")), "open image before failed replacement");
        PortableReaderFrame previousFrame = session.Current;
        WriteableBitmap previousPresentation = session.PresentationBitmap!;
        string previousPath = session.CurrentPath!;
        byte firstPixel = previousFrame.Image!.GetRowSpan(0)[0];
        True(!session.Open(Fixture("corrupt-image.png")), "corrupt replacement is rejected");
        Equal(PortablePlayerStatus.Open, session.Status, "failed replacement preserves open session");
        Same(previousFrame, session.Current, "failed replacement preserves current frame wrapper");
        Same(previousPresentation, session.PresentationBitmap, "failed replacement preserves presentation bitmap");
        Equal(previousPath, session.CurrentPath, "failed replacement preserves current path");
        Equal(firstPixel, session.Current.Image!.GetRowSpan(0)[0], "failed replacement preserves readable pixels");
        True(session.ErrorMessage is not null, "failed replacement reports an error without discarding the current image");
        using (ILockedFramebuffer _ = previousPresentation.Lock()) { }
        True(!session.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")), "missing replacement is rejected");
        Equal(PortablePlayerStatus.Open, session.Status, "missing replacement preserves open session");
        Same(previousFrame, session.Current, "missing replacement preserves current frame wrapper");
        session.Close();
        Throws<ObjectDisposedException>(() => _ = previousFrame.Image!.Memory.Length, "close after failed replacement releases old pixels");
        Throws<ObjectDisposedException>(() => previousPresentation.Lock(), "close after failed replacement disposes old presentation bitmap");

        True(session.Open(Fixture("bmp-rgb-7x5.bmp")), "open source before successful replacement");
        PortableReaderFrame replacedFrame = session.Current;
        WriteableBitmap replacedBitmap = session.PresentationBitmap!;
        bool changeObservedNewSession = false;
        EventHandler handler = (_, _) =>
        {
            if (session.Status == PortablePlayerStatus.Open && session.CurrentPath == Fixture("png-alpha-13x9.png"))
            {
                using ILockedFramebuffer _ = session.PresentationBitmap!.Lock();
                using ILockedFramebuffer __ = replacedBitmap.Lock();
                changeObservedNewSession = true;
            }
        };
        session.Changed += handler;
        True(session.Open(Fixture("png-alpha-13x9.png")), "successful replacement opens new image");
        session.Changed -= handler;
        True(changeObservedNewSession, "replacement event exposes new presentation before previous disposal");
        Throws<ObjectDisposedException>(() => _ = replacedFrame.Image!.Memory.Length, "successful replacement releases prior reader pixels");
        Throws<ObjectDisposedException>(() => replacedBitmap.Lock(), "successful replacement disposes prior presentation bitmap");
    }

    private static int Measure(string path)
    {
        var samples = new List<double>();
        var allocated = new List<long>();
        for (int i = 0; i < 7; i++)
        {
            using var session = new PortablePlayerSession();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            if (!session.Open(path)) throw new InvalidOperationException("Measurement image did not open.");
            timer.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (i >= 2)
            {
                samples.Add(timer.Elapsed.TotalMilliseconds);
                allocated.Add(bytes);
            }
        }
        samples.Sort();
        allocated.Sort();
        Console.WriteLine($"open+decode+present samples={samples.Count}, medianMs={samples[samples.Count / 2]:F3}, medianManagedAllocatedBytes={allocated[allocated.Count / 2]}, presentationCopies=1/open");
        return 0;
    }

    private static void VerifyBitmapRows(WriteableBitmap bitmap, PortableImageFrame source)
    {
        using ILockedFramebuffer frame = bitmap.Lock();
        int rowBytes = source.Format == Kinovea.PortablePixels.PixelFormat.Rgb24 ? source.Width * 3 : source.Width * 4;
        Equal(source.Width, frame.Size.Width, "presentation framebuffer width");
        Equal(source.Height, frame.Size.Height, "presentation framebuffer height");
        True(frame.RowBytes >= rowBytes, "presentation framebuffer stride covers row");
        var actual = new byte[rowBytes];
        for (int y = 0; y < source.Height; y++)
        {
            Marshal.Copy(IntPtr.Add(frame.Address, checked(y * frame.RowBytes)), actual, 0, rowBytes);
            True(actual.AsSpan().SequenceEqual(source.GetRowSpan(y)), $"presentation logical row {y} matches source");
        }
    }

    private static byte[] ReadBitmapPixel(WriteableBitmap bitmap, int x, int y, int bytesPerPixel)
    {
        using ILockedFramebuffer frame = bitmap.Lock();
        var row = new byte[frame.RowBytes];
        Marshal.Copy(IntPtr.Add(frame.Address, checked(y * frame.RowBytes)), row, 0, row.Length);
        return row.AsSpan(checked(x * bytesPerPixel), bytesPerPixel).ToArray();
    }

    private static void True(bool value, string name) { assertions++; if (!value) throw new InvalidOperationException(name + ": expected true."); }
    private static void Equal<T>(T expected, T actual, string name) { assertions++; if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'."); }
    private static void Near(double expected, double actual, double epsilon, string name) { assertions++; if (Math.Abs(expected - actual) > epsilon) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}."); }
    private static void Same(object? expected, object? actual, string name) { assertions++; if (!ReferenceEquals(expected, actual)) throw new InvalidOperationException(name + ": references differ."); }
    private static void Throws<TException>(Action action, string name) where TException : Exception { assertions++; try { action(); } catch (TException) { return; } throw new InvalidOperationException(name + ": expected " + typeof(TException).Name + "."); }
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private sealed class TestApp : Application { }
}
