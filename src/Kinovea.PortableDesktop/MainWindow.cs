using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kinovea.PortableDesktop.Presentation;
using Kinovea.PortableReaderShell;

namespace Kinovea.PortableDesktop;

internal sealed class MainWindow : Window
{
    private readonly PortablePlayerSession session = new();
    private readonly ImageViewport viewport = new();
    private readonly TextBlock name = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock metadata = new();
    private readonly TextBlock status = new() { Text = "Open a BMP or PNG image to begin." };
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 249, SmallChange = 1, LargeChange = 10, Width = 240, IsEnabled = false };
    private readonly Button previous = new() { Content = "◀" };
    private readonly Button next = new() { Content = "▶" };
    private readonly string? initialPath;
    private readonly string? smokeReportPath;
    private bool updatingTimeline;
    private bool visibleFrameRendered;

    public MainWindow(string[] args)
    {
        Title = "Kinovea Native Player";
        Width = 1120;
        Height = 760;
        MinWidth = 720;
        MinHeight = 480;
        Background = Brush("#20252c");
        initialPath = Argument(args, "--open");
        smokeReportPath = Argument(args, "--smoke-report");
        Content = BuildLayout();

        session.Changed += SessionChanged;
        viewport.ImageRendered += () =>
        {
            visibleFrameRendered = true;
            WriteSmokeReport();
        };
        Opened += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(initialPath))
                await OpenPathAsync(initialPath);
        };
        KeyDown += WindowKeyDown;
        Closed += (_, _) => session.Dispose();
        Refresh();
    }

    private Control BuildLayout()
    {
        var open = new Button { Content = "Open image…", Margin = new Thickness(4) };
        var close = new Button { Content = "Close", Margin = new Thickness(4), IsEnabled = false };
        var fit = new Button { Content = "Fit", Margin = new Thickness(4) };
        var zoomOut = new Button { Content = "−", Margin = new Thickness(4) };
        var zoomIn = new Button { Content = "+", Margin = new Thickness(4) };
        var zoomLabel = new TextBlock { Text = "Fit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        var emptyHint = new TextBlock
        {
            Text = "Open a BMP or PNG image",
            Foreground = Brush("#9aa4b2"),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        open.Click += async (_, _) => await PickAndOpenAsync();
        close.Click += (_, _) => session.Close();
        fit.Click += (_, _) => { viewport.Fit(); zoomLabel.Text = "Fit"; };
        zoomOut.Click += (_, _) => { viewport.ChangeZoom(viewport.Zoom / 1.2); zoomLabel.Text = $"{viewport.Zoom:P0}"; };
        zoomIn.Click += (_, _) => { viewport.ChangeZoom(viewport.Zoom * 1.2); zoomLabel.Text = $"{viewport.Zoom:P0}"; };
        previous.Click += (_, _) => session.StepBackward();
        next.Click += (_, _) => session.StepForward();
        timeline.ValueChanged += (_, _) =>
        {
            if (!updatingTimeline && session.Status == PortablePlayerStatus.Open)
                session.Seek((long)Math.Round(timeline.Value));
        };
        session.Changed += (_, _) =>
        {
            close.IsEnabled = session.Status == PortablePlayerStatus.Open;
            previous.IsEnabled = session.CanStepBackward;
            next.IsEnabled = session.CanStepForward;
            emptyHint.IsVisible = session.Status != PortablePlayerStatus.Open;
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(open);
        toolbar.Children.Add(close);
        toolbar.Children.Add(new Separator { Margin = new Thickness(8, 4) });
        toolbar.Children.Add(previous);
        toolbar.Children.Add(next);
        toolbar.Children.Add(timeline);
        toolbar.Children.Add(fit);
        toolbar.Children.Add(zoomOut);
        toolbar.Children.Add(zoomIn);
        toolbar.Children.Add(zoomLabel);

        var header = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 8) };
        header.Children.Add(name);
        header.Children.Add(metadata);
        header.Children.Add(status);

        var top = new StackPanel { Orientation = Orientation.Vertical };
        var toolbarBorder = new Border { Background = Brush("#2b323b"), Child = toolbar, Padding = new Thickness(6) };
        top.Children.Add(toolbarBorder);
        var headerBorder = new Border { Background = Brush("#252b33"), Child = header };
        top.Children.Add(headerBorder);

        var canvas = new Grid();
        canvas.Children.Add(viewport);
        canvas.Children.Add(emptyHint);
        var content = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        content.Children.Add(top);
        content.Children.Add(canvas);
        return content;
    }

    private async Task PickAndOpenAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open still image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("BMP and PNG images") { Patterns = ["*.bmp", "*.png"] },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            await OpenPathAsync(path);
    }

    private Task OpenPathAsync(string path)
    {
        bool opened = session.Open(path);
        if (!opened)
            WriteSmokeReport();
        Refresh();
        return Task.CompletedTask;
    }

    private void SessionChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            if (!ReferenceEquals(viewport.Bitmap, session.PresentationBitmap))
            {
                visibleFrameRendered = false;
                viewport.SetBitmap(session.PresentationBitmap);
            }
            Refresh();
            if (session.Status != PortablePlayerStatus.Open)
                WriteSmokeReport();
        }

        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    private void Refresh()
    {
        PortableReaderMetadata? info = session.Metadata;
        bool loaded = session.Status == PortablePlayerStatus.Open && info is not null;
        name.Text = loaded ? Path.GetFileNameWithoutExtension(session.CurrentPath) : "Kinovea";
        metadata.Text = loaded
            ? $"{info!.Width} × {info.Height} px    {info.FramesPerSecond:0.00} fps    Frame {session.Current.Timestamp} / {session.TimelineEnd}    Previous {session.Current.PreviousTimestamp}"
            : string.Empty;
        status.Text = session.ErrorMessage ?? (loaded ? "Still image · paused" : "Open a BMP or PNG image to begin.");
        timeline.Minimum = session.TimelineStart;
        timeline.Maximum = Math.Max(session.TimelineStart, session.TimelineEnd);
        timeline.IsEnabled = loaded;
        updatingTimeline = true;
        timeline.Value = loaded ? Math.Clamp(session.Current.Timestamp, session.TimelineStart, session.TimelineEnd) : 0;
        updatingTimeline = false;
        viewport.InvalidateVisual();
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.O)
        {
            _ = PickAndOpenAsync();
            e.Handled = true;
        }
        else if ((control && e.Key == Key.W) || e.Key == Key.Escape)
        {
            session.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            session.StepForward();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            session.StepBackward();
            e.Handled = true;
        }
        else if (e.Key == Key.Home && session.Status == PortablePlayerStatus.Open)
        {
            session.Seek(session.TimelineStart);
            e.Handled = true;
        }
        else if (e.Key == Key.End && session.Status == PortablePlayerStatus.Open)
        {
            session.Seek(session.TimelineEnd);
            e.Handled = true;
        }
    }

    private void WriteSmokeReport()
    {
        if (smokeReportPath is null || session.Status == PortablePlayerStatus.Open && session.Current.Image is null)
            return;
        var info = session.Metadata;
        string? inputHash = session.CurrentPath is not null && File.Exists(session.CurrentPath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(session.CurrentPath))).ToLowerInvariant()
            : null;
        var report = new
        {
            schemaVersion = 1,
            status = visibleFrameRendered && session.Status == PortablePlayerStatus.Open ? "VISIBLE_FRAME_RENDERED" : session.Status.ToString().ToUpperInvariant(),
            sourceSha = Environment.GetEnvironmentVariable("KINOVEA_SOURCE_SHA") ?? "LOCAL_UNRECORDED",
            fileName = session.CurrentPath is null ? null : Path.GetFileName(session.CurrentPath),
            inputSha256 = inputHash,
            width = info?.Width,
            height = info?.Height,
            fps = info?.FramesPerSecond,
            timestamp = session.Current.Timestamp,
            previousTimestamp = session.Current.PreviousTimestamp,
            workingZoneStart = session.TimelineStart,
            workingZoneEnd = session.TimelineEnd,
            pixelFormat = session.Current.Image?.Format.ToString(),
            alphaMode = session.Current.Image?.AlphaMode.ToString(),
            readerState = session.Status.ToString(),
            presentationCopyCount = session.PresentationCopies,
            presentationBytesCopied = session.PresentationBytesCopied,
            processId = Environment.ProcessId
        };
        string fullPath = Path.GetFullPath(smokeReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? Argument(string[] args, string key)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == key)
                return args[i + 1];
        return null;
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
