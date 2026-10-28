using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kinovea.PortableDesktop.Presentation;
using Kinovea.PortableReaderShell;
using Kinovea.PortableVideo;

namespace Kinovea.PortableDesktop;

internal sealed class MainWindow : Window
{
    private readonly PortablePlayerSession session = new();
    private readonly PortableVideoPlayerSession videoSession = new();
    private readonly ImageViewport viewport = new();
    private readonly TextBlock name = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock metadata = new();
    private readonly TextBlock status = new() { Text = "Open a BMP or PNG image to begin." };
    private TextBlock? emptyHintText;
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 249, SmallChange = 1, LargeChange = 10, Width = 240, IsEnabled = false };
    private readonly Button previous = new() { Content = "◀" };
    private readonly Button next = new() { Content = "▶" };
    private readonly TextBox calibrationLength = new() { Text = "100", Width = 64, PlaceholderText = "length" };
    private readonly TextBox calibrationUnit = new() { Text = "mm", Width = 50, PlaceholderText = "unit" };
    private readonly List<PortableImagePoint> analysisPoints = [];
    private PortableImagePoint? calibrationStart;
    private PortableImagePoint? calibrationEnd;
    private PortableDistanceAnalysis? distanceAnalysis;
    private long? analysisTimestamp;
    private bool collectingCalibration;
    private bool collectingMeasurement;
    private Button? openButton;
    private Button? closeButton;
    private Button? fitButton;
    private Button? playPauseButton;
    private Button? calibrateButton;
    private Button? measureButton;
    private Button? saveAnalysisButton;
    private Button? openAnalysisButton;
    private Button? saveLegacyKvaButton;
    private PortableKvaDocument? legacyKvaDocument;
    private string? lastLegacyKvaSavePath;
    private string? lastAnalysisPath;
    private readonly string? initialPath;
    private readonly string? initialVideoPath;
    private readonly string? smokeReportPath;
    private bool updatingTimeline;
    private bool visibleFrameRendered;
    private string? lastPickerResult;
    private string? lastOpenResult;
    private string? lastAttemptedFileName;
    private bool activeVideo;
    private bool videoTickRunning;
    private readonly DispatcherTimer videoTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public MainWindow(string[] args)
    {
        Title = "Kinovea Native Player";
        Width = 1120;
        Height = 760;
        MinWidth = 720;
        MinHeight = 480;
        Background = Brush("#20252c");
        initialPath = Argument(args, "--open");
        initialVideoPath = Argument(args, "--open-video");
        smokeReportPath = Argument(args, "--smoke-report");
        Content = BuildLayout();

        session.Changed += SessionChanged;
        videoSession.Changed += VideoSessionChanged;
        videoTimer.Tick += VideoTimerTick;
        viewport.ImageRendered += () =>
        {
            visibleFrameRendered = true;
            WriteSmokeReport();
        };
        viewport.InteractionObserved += WriteSmokeReport;
        viewport.AnalysisPointSelected += AnalysisPointSelected;
        Opened += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                lastPickerResult = "CommandLine";
                await OpenPathAsync(initialPath);
            }
            else if (!string.IsNullOrWhiteSpace(initialVideoPath))
            {
                lastPickerResult = "CommandLine";
                await OpenPathAsync(initialVideoPath);
            }
            else
                WriteSmokeReport();
        };
        KeyDown += WindowKeyDown;
        Closed += (_, _) => session.Dispose();
        Closed += async (_, _) => await videoSession.DisposeAsync();
        Refresh();
    }

    private Control BuildLayout()
    {
        var open = openButton = new Button { Content = "Open image…", Margin = new Thickness(4) };
        var close = closeButton = new Button { Content = "Close", Margin = new Thickness(4), IsEnabled = false };
        var play = playPauseButton = new Button { Content = "Play", Margin = new Thickness(4), IsEnabled = false };
        var fit = fitButton = new Button { Content = "Fit", Margin = new Thickness(4) };
        var calibrate = calibrateButton = new Button { Content = "Calibrate", Margin = new Thickness(4) };
        var measure = measureButton = new Button { Content = "Measure", Margin = new Thickness(4) };
        var saveAnalysis = saveAnalysisButton = new Button { Content = "Save analysis…", Margin = new Thickness(4) };
        var openAnalysis = openAnalysisButton = new Button { Content = "Open analysis…", Margin = new Thickness(4) };
        var saveLegacyKva = saveLegacyKvaButton = new Button { Content = "Save legacy KVA…", Margin = new Thickness(4), IsEnabled = false };
        var zoomOut = new Button { Content = "−", Margin = new Thickness(4) };
        var zoomIn = new Button { Content = "+", Margin = new Thickness(4) };
        var zoomLabel = new TextBlock { Text = "Fit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        viewport.ZoomChanged += zoom => zoomLabel.Text = zoom == 1 ? "Fit" : $"{zoom:P0}";
        var emptyHint = emptyHintText = new TextBlock
        {
            Text = "Open a BMP, PNG, JPEG, GIF, SVG, or video file",
            Foreground = Brush("#9aa4b2"),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        open.Click += async (_, _) => await PickAndOpenAsync();
        close.Click += async (_, _) => await CloseActiveAsync();
        play.Click += (_, _) => TogglePlayback();
        fit.Click += (_, _) => { viewport.Fit(); zoomLabel.Text = "Fit"; };
        zoomOut.Click += (_, _) => { viewport.ChangeZoom(viewport.Zoom / 1.2); zoomLabel.Text = $"{viewport.Zoom:P0}"; };
        zoomIn.Click += (_, _) => { viewport.ChangeZoom(viewport.Zoom * 1.2); zoomLabel.Text = $"{viewport.Zoom:P0}"; };
        previous.Click += async (_, _) => await StepBackwardAsync();
        next.Click += async (_, _) => await StepForwardAsync();
        calibrate.Click += (_, _) => ArmCalibration();
        measure.Click += (_, _) => ArmMeasurement();
        saveAnalysis.Click += async (_, _) => await SaveAnalysisAsync();
        openAnalysis.Click += async (_, _) => await OpenAnalysisAsync();
        saveLegacyKva.Click += async (_, _) => await SaveLegacyKvaAsync();
        timeline.ValueChanged += (_, _) =>
        {
            if (updatingTimeline) return;
            if (activeVideo && videoSession.Status == PortablePlayerStatus.Open)
                _ = videoSession.SeekAsync((long)Math.Round(timeline.Value));
            else if (session.Status == PortablePlayerStatus.Open)
                session.Seek((long)Math.Round(timeline.Value));
        };
        session.Changed += (_, _) =>
        {
            if (activeVideo) return;
            close.IsEnabled = session.Status == PortablePlayerStatus.Open;
            previous.IsEnabled = session.CanStepBackward;
            next.IsEnabled = session.CanStepForward;
            emptyHint.IsVisible = session.Status != PortablePlayerStatus.Open;
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(open);
        toolbar.Children.Add(close);
        toolbar.Children.Add(play);
        toolbar.Children.Add(new Separator { Margin = new Thickness(8, 4) });
        toolbar.Children.Add(previous);
        toolbar.Children.Add(next);
        toolbar.Children.Add(timeline);
        toolbar.Children.Add(fit);
        toolbar.Children.Add(zoomOut);
        toolbar.Children.Add(zoomIn);
        toolbar.Children.Add(zoomLabel);
        var analysisToolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        analysisToolbar.Children.Add(new TextBlock { Text = "Analysis:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) });
        analysisToolbar.Children.Add(calibrationLength);
        analysisToolbar.Children.Add(calibrationUnit);
        analysisToolbar.Children.Add(calibrate);
        analysisToolbar.Children.Add(measure);
        analysisToolbar.Children.Add(saveAnalysis);
        analysisToolbar.Children.Add(openAnalysis);
        analysisToolbar.Children.Add(saveLegacyKva);

        var header = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 8) };
        header.Children.Add(name);
        header.Children.Add(metadata);
        header.Children.Add(status);

        var top = new StackPanel { Orientation = Orientation.Vertical };
        var toolbarBorder = new Border { Background = Brush("#2b323b"), Child = toolbar, Padding = new Thickness(6) };
        top.Children.Add(toolbarBorder);
        var analysisToolbarBorder = new Border { Background = Brush("#303741"), Child = analysisToolbar, Padding = new Thickness(4) };
        top.Children.Add(analysisToolbarBorder);
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
        lastPickerResult = "Opening";
        lastOpenResult = null;
        WriteSmokeReport();
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open image or video",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images, video, and Kinovea analysis") { Patterns = ["*.bmp", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v", "*.kva"] },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count != 1)
        {
            lastPickerResult = "Cancelled";
            WriteSmokeReport();
            return;
        }

        lastPickerResult = "Selected";
        if (files[0].TryGetLocalPath() is { } path)
            await OpenPathAsync(path);
        else
        {
            lastOpenResult = "SelectedFileHasNoLocalPath";
            lastAttemptedFileName = files[0].Name;
            WriteSmokeReport();
        }
    }

    private async Task OpenPathAsync(string path)
    {
        lastAttemptedFileName = Path.GetFileName(path);
        if (string.Equals(Path.GetExtension(path), ".kva", StringComparison.OrdinalIgnoreCase))
        {
            await OpenLegacyKvaAsync(path);
            return;
        }
        await OpenMediaPathAsync(path, fromKva: false);
    }

    private async Task OpenMediaPathAsync(string path, bool fromKva)
    {
        bool opened;
        if (IsVideoPath(path))
        {
            opened = await videoSession.OpenAsync(path);
            if (opened)
            {
                if (session.Status == PortablePlayerStatus.Open)
                    session.Close();
                activeVideo = true;
                distanceAnalysis = null;
                analysisTimestamp = null;
                calibrationStart = calibrationEnd = null;
                viewport.Analysis = null;
                if (emptyHintText is not null) emptyHintText.IsVisible = false;
                viewport.SetBitmap(videoSession.PresentationBitmap);
            }
        }
        else
        {
            opened = session.Open(path);
            if (opened)
            {
                if (activeVideo)
                    await videoSession.CloseAsync();
                activeVideo = false;
                distanceAnalysis = null;
                analysisTimestamp = null;
                calibrationStart = calibrationEnd = null;
                viewport.Analysis = null;
                viewport.SetBitmap(session.PresentationBitmap);
            }
        }
        if (opened && !fromKva)
        {
            legacyKvaDocument = null;
            if (saveLegacyKvaButton is not null) saveLegacyKvaButton.IsEnabled = false;
        }
        lastOpenResult = opened ? "Opened" : "Rejected";
        if (opened)
            visibleFrameRendered = false;
        if (!opened)
            WriteSmokeReport();
        Refresh();
    }

    private async Task OpenLegacyKvaAsync(string kvaPath)
    {
        try
        {
            PortableKvaDocument document = PortableKvaDocument.Load(kvaPath);
            string? mediaPath = document.ResolveMediaPath();
            bool mediaMissing = mediaPath is null || !File.Exists(mediaPath);
            bool mediaLengthMismatch = !mediaMissing && document.ByteLength is long storedLength && storedLength > 0 &&
                new FileInfo(mediaPath!).Length != storedLength;
            if (mediaMissing || mediaLengthMismatch)
            {
                status.Text = mediaLengthMismatch
                    ? "KVA media byte length differs from its recorded reference. Select the intended source to relink it."
                    : "KVA has no available media path. Select the source video or image to relink it.";
                IReadOnlyList<IStorageFile> selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Relink media referenced by KVA",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Images and video") { Patterns = ["*.bmp", "*.png", "*.jpg", "*.jpeg", "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v"] }, FilePickerFileTypes.All]
                });
                if (selected.Count != 1 || selected[0].TryGetLocalPath() is not string selectedPath) return;
                var mediaInfo = new FileInfo(selectedPath);
                document = document.RelinkMedia(selectedPath, mediaInfo.Length);
                mediaPath = selectedPath;
            }

            await OpenMediaPathAsync(mediaPath!, fromKva: true);
            if (lastOpenResult != "Opened") return;
            legacyKvaDocument = document;
            if (saveLegacyKvaButton is not null) saveLegacyKvaButton.IsEnabled = distanceAnalysis is null;
            Refresh();
            WriteSmokeReport();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status.Text = $"Could not open KVA ({exception.GetType().Name}): {exception.Message}";
            lastOpenResult = "Rejected";
            WriteSmokeReport();
        }
    }

    private async Task SaveLegacyKvaAsync()
    {
        if (legacyKvaDocument is null) return;
        if (distanceAnalysis is not null)
        {
            status.Text = "This KVA contains preserved legacy data. Save newly created portable measurements as a .kinovea.json sidecar; merging them into KVA is not supported.";
            return;
        }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Kinovea analysis XML",
            SuggestedFileName = Path.GetFileName(legacyKvaDocument.SourcePath),
            DefaultExtension = "kva",
            FileTypeChoices = [new FilePickerFileType("Kinovea analysis") { Patterns = ["*.kva"] }]
        });
        string? path = file?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            await legacyKvaDocument.SaveAsync(path);
            lastLegacyKvaSavePath = path;
            status.Text = $"KVA document saved; XML sections preserved ({legacyKvaDocument.FormatVersion}).";
            WriteSmokeReport();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status.Text = $"Could not save KVA ({exception.GetType().Name}): {exception.Message}";
            WriteSmokeReport();
        }
    }

    private void SessionChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            if (activeVideo) return;
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

    private void VideoSessionChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            if (!activeVideo) return;
            if (!ReferenceEquals(viewport.Bitmap, videoSession.PresentationBitmap))
            {
                visibleFrameRendered = false;
                viewport.SetBitmap(videoSession.PresentationBitmap);
            }
            if (emptyHintText is not null)
                emptyHintText.IsVisible = videoSession.Status != PortablePlayerStatus.Open;
            Refresh();
            if (videoSession.Status != PortablePlayerStatus.Open)
                WriteSmokeReport();
        }

        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }

    private void Refresh()
    {
        if (activeVideo)
        {
            viewport.Analysis = analysisTimestamp == videoSession.CurrentTimestamp ? distanceAnalysis : null;
            PortableVideoMetadata? video = videoSession.Metadata;
            bool videoLoaded = videoSession.Status == PortablePlayerStatus.Open && video is not null;
            name.Text = videoLoaded ? Path.GetFileNameWithoutExtension(videoSession.CurrentPath) : "Kinovea";
            metadata.Text = videoLoaded
                ? $"{video!.Width} × {video.Height} px    {video.AverageFrameRate:0.00} fps    PTS {videoSession.CurrentTimestamp} / {videoSession.TimelineEnd}"
                : string.Empty;
            status.Text = videoSession.ErrorMessage ?? (videoLoaded
                ? analysisTimestamp == videoSession.CurrentTimestamp && distanceAnalysis is { } analysis
                    ? $"Analysis · {analysis.MeasuredDistance:0.###} {analysis.Unit} at PTS {analysisTimestamp}"
                    : videoSession.IsPlaying ? "Video · playing" : "Video · paused"
                : "Open a supported image or video file to begin.");
            if (videoLoaded && legacyKvaDocument is { } videoKva)
                status.Text = $"Legacy KVA v{videoKva.FormatVersion} loaded; {videoKva.KeyframeCount} keyframes, {videoKva.RecognizedDrawingCount} recognized drawing records retained. Legacy drawing rendering/timestamp remapping is not applied.";
            timeline.Minimum = videoSession.TimelineStart;
            timeline.Maximum = Math.Max(videoSession.TimelineStart, videoSession.TimelineEnd);
            timeline.IsEnabled = videoLoaded;
            updatingTimeline = true;
            timeline.Value = videoLoaded ? Math.Clamp(videoSession.CurrentTimestamp, timeline.Minimum, timeline.Maximum) : 0;
            updatingTimeline = false;
            if (closeButton is not null) closeButton.IsEnabled = videoLoaded;
            if (playPauseButton is not null)
            {
                playPauseButton.IsEnabled = videoLoaded;
                playPauseButton.Content = videoSession.IsPlaying ? "Pause" : "Play";
            }
            previous.IsEnabled = videoLoaded && videoSession.CanStepBackward;
            next.IsEnabled = videoLoaded && videoSession.CanStepForward;
            viewport.InvalidateVisual();
            return;
        }

        PortableReaderMetadata? info = session.Metadata;
        bool loaded = session.Status == PortablePlayerStatus.Open && info is not null;
        name.Text = loaded ? Path.GetFileNameWithoutExtension(session.CurrentPath) : "Kinovea";
        metadata.Text = loaded
            ? $"{info!.Width} × {info.Height} px    {info.FramesPerSecond:0.00} fps    Frame {session.Current.Timestamp} / {session.TimelineEnd}    Previous {session.Current.PreviousTimestamp}"
            : string.Empty;
        status.Text = session.ErrorMessage ?? (loaded ? "Still image · paused" : "Open a BMP, PNG, or JPEG image to begin.");
        if (loaded && legacyKvaDocument is { } imageKva)
            status.Text = $"Legacy KVA v{imageKva.FormatVersion} loaded; {imageKva.KeyframeCount} keyframes, {imageKva.RecognizedDrawingCount} recognized drawing records retained. Legacy drawing rendering is not applied.";
        timeline.Minimum = session.TimelineStart;
        timeline.Maximum = Math.Max(session.TimelineStart, session.TimelineEnd);
        timeline.IsEnabled = loaded;
        updatingTimeline = true;
        timeline.Value = loaded ? Math.Clamp(session.Current.Timestamp, session.TimelineStart, session.TimelineEnd) : 0;
        updatingTimeline = false;
        if (playPauseButton is not null)
        {
            playPauseButton.IsEnabled = false;
            playPauseButton.Content = "Play";
        }
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
            _ = CloseActiveAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _ = StepForwardAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            _ = StepBackwardAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Home && (activeVideo ? videoSession.Status : session.Status) == PortablePlayerStatus.Open)
        {
            if (activeVideo) _ = videoSession.SeekAsync(videoSession.TimelineStart);
            else session.Seek(session.TimelineStart);
            e.Handled = true;
        }
        else if (e.Key == Key.End && (activeVideo ? videoSession.Status : session.Status) == PortablePlayerStatus.Open)
        {
            if (activeVideo) _ = videoSession.SeekAsync(videoSession.TimelineEnd);
            else session.Seek(session.TimelineEnd);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && activeVideo)
        {
            TogglePlayback();
            e.Handled = true;
        }
    }

    private async Task StepForwardAsync()
    {
        if (activeVideo)
        {
            videoSession.Pause();
            videoTimer.Stop();
            await videoSession.StepForwardAsync();
        }
        else
            session.StepForward();
    }

    private async Task StepBackwardAsync()
    {
        if (activeVideo)
        {
            videoSession.Pause();
            videoTimer.Stop();
            await videoSession.StepBackwardAsync();
        }
        else
            session.StepBackward();
    }

    private async Task CloseActiveAsync()
    {
        videoTimer.Stop();
        if (activeVideo)
        {
            await videoSession.CloseAsync();
            activeVideo = false;
            distanceAnalysis = null;
            analysisTimestamp = null;
            calibrationStart = calibrationEnd = null;
            viewport.Analysis = null;
            viewport.SetBitmap(session.PresentationBitmap);
        }
        else
            session.Close();
        legacyKvaDocument = null;
        if (saveLegacyKvaButton is not null) saveLegacyKvaButton.IsEnabled = false;
        Refresh();
        WriteSmokeReport();
    }

    private void TogglePlayback()
    {
        if (!activeVideo) return;
        if (videoSession.IsPlaying)
        {
            videoSession.Pause();
            videoTimer.Stop();
        }
        else
        {
            videoSession.Play();
            videoTimer.Start();
        }
        Refresh();
    }

    private void ArmCalibration()
    {
        if (!activeVideo || videoSession.Metadata is null)
        {
            status.Text = "Open a video before calibrating a distance.";
            return;
        }
        if (!double.TryParse(calibrationLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double knownLength)
            || !double.IsFinite(knownLength) || knownLength <= 0 || string.IsNullOrWhiteSpace(calibrationUnit.Text))
        {
            status.Text = "Enter a positive calibration length and unit first.";
            return;
        }
        collectingCalibration = true;
        videoSession.Pause();
        videoTimer.Stop();
        collectingMeasurement = false;
        analysisPoints.Clear();
        viewport.ArmCalibration();
        status.Text = "Click the two endpoints of the known calibration distance.";
        WriteSmokeReport();
    }

    private void ArmMeasurement()
    {
        if (!activeVideo || calibrationStart is null || calibrationEnd is null)
        {
            status.Text = "Set a calibration before measuring.";
            return;
        }
        collectingCalibration = false;
        videoSession.Pause();
        videoTimer.Stop();
        collectingMeasurement = true;
        analysisPoints.Clear();
        viewport.ArmMeasurement();
        status.Text = "Click the two endpoints of the distance to measure.";
        WriteSmokeReport();
    }

    private void AnalysisPointSelected(Point point)
    {
        if (!activeVideo || videoSession.Metadata is null) return;
        analysisPoints.Add(new PortableImagePoint(point.X, point.Y));
        if (analysisPoints.Count < 2)
        {
            status.Text = "Select the second endpoint.";
            WriteSmokeReport();
            return;
        }

        viewport.CancelAnalysisTool();
        if (collectingCalibration)
        {
            calibrationStart = analysisPoints[0];
            calibrationEnd = analysisPoints[1];
            collectingCalibration = false;
            distanceAnalysis = null;
            viewport.Analysis = null;
            viewport.InvalidateVisual();
            status.Text = $"Calibration set: {PortableAnalysisWorkspaceStore.Distance(calibrationStart.Value, calibrationEnd.Value):0.###} px = {calibrationLength.Text} {calibrationUnit.Text}. Now choose Measure.";
            WriteSmokeReport();
            return;
        }
        WriteSmokeReport();

        if (collectingMeasurement && calibrationStart is PortableImagePoint start && calibrationEnd is PortableImagePoint end
            && double.TryParse(calibrationLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double knownLength))
        {
            try
            {
                distanceAnalysis = PortableDistanceAnalysisFactory.Create(start, end, knownLength, calibrationUnit.Text ?? "mm",
                    analysisPoints[0], analysisPoints[1]);
                if (saveLegacyKvaButton is not null) saveLegacyKvaButton.IsEnabled = false;
                analysisTimestamp = videoSession.CurrentTimestamp;
                viewport.Analysis = distanceAnalysis;
                viewport.InvalidateVisual();
                status.Text = $"Measured {distanceAnalysis.MeasuredDistance:0.###} {distanceAnalysis.Unit} at PTS {videoSession.CurrentTimestamp}.";
            }
            catch (ArgumentException exception)
            {
                status.Text = exception.Message;
            }
            collectingMeasurement = false;
        }
    }

    private async Task SaveAnalysisAsync()
    {
        if (!activeVideo || distanceAnalysis is null || videoSession.Metadata is not { } metadata || videoSession.CurrentPath is not string mediaPath)
        {
            status.Text = "Open a video, calibrate it, and add a measurement before saving.";
            return;
        }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Kinovea portable analysis",
            SuggestedFileName = Path.GetFileNameWithoutExtension(mediaPath) + ".kinovea.json",
            DefaultExtension = "kinovea.json",
            FileTypeChoices = [new FilePickerFileType("Kinovea portable analysis") { Patterns = ["*.kinovea.json"] }]
        });
        string? workspacePath = file?.TryGetLocalPath();
        if (workspacePath is null) return;
        try
        {
            await PortableAnalysisWorkspaceStore.SaveAsync(workspacePath, mediaPath, metadata.Width, metadata.Height,
                analysisTimestamp ?? videoSession.CurrentTimestamp, distanceAnalysis);
            lastAnalysisPath = workspacePath;
            status.Text = $"Analysis saved to {Path.GetFileName(workspacePath)}.";
            WriteSmokeReport();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status.Text = $"Could not save analysis ({exception.GetType().Name}): {exception.Message}";
            WriteSmokeReport();
        }
    }

    private async Task OpenAnalysisAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Kinovea portable analysis",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Kinovea portable analysis") { Patterns = ["*.kinovea.json"] }]
        });
        if (files.Count != 1 || files[0].TryGetLocalPath() is not string workspacePath) return;
        try
        {
            (PortableAnalysisWorkspace workspace, string mediaPath) = await PortableAnalysisWorkspaceStore.LoadAsync(workspacePath);
            using (var probe = new PortableVideoReader())
            {
                probe.Open(mediaPath);
                if (probe.Metadata is not { } probeMetadata || probeMetadata.Width != workspace.Width || probeMetadata.Height != workspace.Height)
                    throw new InvalidDataException("Workspace media dimensions do not match the saved analysis.");
            }
            if (!await videoSession.OpenAsync(mediaPath, workspace.Timestamp))
            {
                status.Text = videoSession.ErrorMessage ?? "The workspace video could not be opened.";
                return;
            }
            distanceAnalysis = null;
            analysisTimestamp = null;
            calibrationStart = calibrationEnd = null;
            viewport.Analysis = null;
            if (videoSession.Metadata is not { } metadata || metadata.Width != workspace.Width || metadata.Height != workspace.Height)
            {
                await videoSession.CloseAsync();
                activeVideo = false;
                viewport.SetBitmap(session.PresentationBitmap);
                status.Text = "Workspace video dimensions do not match the saved analysis; it was closed.";
                Refresh();
                return;
            }
            activeVideo = true;
            legacyKvaDocument = null;
            if (saveLegacyKvaButton is not null) saveLegacyKvaButton.IsEnabled = false;
            if (session.Status == PortablePlayerStatus.Open) session.Close();
            if (emptyHintText is not null) emptyHintText.IsVisible = false;
            viewport.SetBitmap(videoSession.PresentationBitmap);
            distanceAnalysis = workspace.DistanceAnalysis;
            lastAnalysisPath = workspacePath;
            analysisTimestamp = workspace.Timestamp;
            calibrationStart = distanceAnalysis.CalibrationStart;
            calibrationEnd = distanceAnalysis.CalibrationEnd;
            viewport.Analysis = distanceAnalysis;
            viewport.InvalidateVisual();
            long actualTimestamp = videoSession.CurrentTimestamp;
            status.Text = actualTimestamp == workspace.Timestamp
                ? $"Restored analysis · {distanceAnalysis.MeasuredDistance:0.###} {distanceAnalysis.Unit} at PTS {actualTimestamp}."
                : $"Restored analysis; requested PTS {workspace.Timestamp}, decoder selected PTS {actualTimestamp}.";
            Refresh();
            WriteSmokeReport();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status.Text = $"Could not open analysis ({exception.GetType().Name}): {exception.Message}";
            WriteSmokeReport();
        }
    }

    private async void VideoTimerTick(object? sender, EventArgs e)
    {
        if (!activeVideo || videoTickRunning) return;
        videoTickRunning = true;
        try
        {
            await videoSession.PlaybackTickAsync();
            if (!videoSession.IsPlaying)
                videoTimer.Stop();
            Refresh();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            videoSession.Pause();
            videoTimer.Stop();
            status.Text = $"Playback stopped ({exception.GetType().Name}).";
        }
        finally
        {
            videoTickRunning = false;
        }
    }

    private static bool IsVideoPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v" or ".gif" or ".svg";

    private void WriteSmokeReport()
    {
        if (activeVideo)
        {
            WriteVideoSmokeReport();
            return;
        }
        if (smokeReportPath is null || session.Status == PortablePlayerStatus.Open && session.Current.Image is null)
            return;
        var info = session.Metadata;
        string? inputHash = GetCurrentInputHash();
        Point? viewportOrigin = viewport.TranslatePoint(new Point(0, 0), this);
        var transform = viewport.Transform;
        Size clientSize = ClientSize;
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
            errorMessage = session.ErrorMessage,
            lastPickerResult,
            lastOpenResult,
            lastAttemptedFileName,
            presentationCopyCount = session.PresentationCopies,
            presentationBytesCopied = session.PresentationBytesCopied,
            windowLogicalWidth = clientSize.Width,
            windowLogicalHeight = clientSize.Height,
            renderScaling = RenderScaling,
            viewportLogicalX = viewportOrigin?.X,
            viewportLogicalY = viewportOrigin?.Y,
            viewportLogicalWidth = viewport.Bounds.Width,
            viewportLogicalHeight = viewport.Bounds.Height,
            zoom = viewport.Zoom,
            panX = viewport.Pan.X,
            panY = viewport.Pan.Y,
            wheelEventCount = viewport.WheelEventCount,
            pointerPressCount = viewport.PointerPressCount,
            pointerMoveCount = viewport.PointerMoveCount,
            lastWheelViewportX = viewport.LastWheelPosition.X,
            lastWheelViewportY = viewport.LastWheelPosition.Y,
            imageRectX = transform?.ImageRectangle.X,
            imageRectY = transform?.ImageRectangle.Y,
            imageRectWidth = transform?.ImageRectangle.Width,
            imageRectHeight = transform?.ImageRectangle.Height,
            openButton = ControlBounds(openButton),
            closeButton = ControlBounds(closeButton),
            fitButton = ControlBounds(fitButton),
            calibrateButton = ControlBounds(calibrateButton),
            measureButton = ControlBounds(measureButton),
            saveAnalysisButton = ControlBounds(saveAnalysisButton),
            openAnalysisButton = ControlBounds(openAnalysisButton),
            saveLegacyKvaButton = ControlBounds(saveLegacyKvaButton),
            previousButton = ControlBounds(previous),
            nextButton = ControlBounds(next),
            processId = Environment.ProcessId
        };
        string fullPath = Path.GetFullPath(smokeReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteVideoSmokeReport()
    {
        if (smokeReportPath is null) return;
        PortableVideoMetadata? info = videoSession.Metadata;
        Point? viewportOrigin = viewport.TranslatePoint(new Point(0, 0), this);
        ViewportTransform? transform = viewport.Transform;
        Size clientSize = ClientSize;
        var report = new
        {
            schemaVersion = 1,
            mediaType = "video",
            status = visibleFrameRendered && videoSession.Status == PortablePlayerStatus.Open ? "VISIBLE_FRAME_RENDERED" : videoSession.Status.ToString().ToUpperInvariant(),
            sourceSha = Environment.GetEnvironmentVariable("KINOVEA_SOURCE_SHA") ?? "LOCAL_UNRECORDED",
            fileName = videoSession.CurrentPath is null ? null : Path.GetFileName(videoSession.CurrentPath),
            inputSha256 = HashFile(videoSession.CurrentPath),
            statusMessage = status.Text,
            width = info?.Width,
            height = info?.Height,
            framesPerSecond = info?.AverageFrameRate,
            timestamp = videoSession.CurrentTimestamp,
            timestampTimeBaseNumerator = info?.TimeBaseNumerator,
            timestampTimeBaseDenominator = info?.TimeBaseDenominator,
            previousTimestampSemantics = "last decoded frame in current read sequence; reset on seek",
            workingZoneStart = videoSession.TimelineStart,
            workingZoneEnd = videoSession.TimelineEnd,
            codec = info?.CodecName,
            nativeFfmpegVersions = PortableFfmpegRuntime.Initialize(),
            readerState = videoSession.Status.ToString(),
            errorMessage = videoSession.ErrorMessage,
            isPlaying = videoSession.IsPlaying,
            presentationCopyCount = videoSession.PresentationCopies,
            presentationBytesCopied = videoSession.PresentationBytesCopied,
            windowLogicalWidth = clientSize.Width,
            windowLogicalHeight = clientSize.Height,
            renderScaling = RenderScaling,
            viewportLogicalX = viewportOrigin?.X,
            viewportLogicalY = viewportOrigin?.Y,
            viewportLogicalWidth = viewport.Bounds.Width,
            viewportLogicalHeight = viewport.Bounds.Height,
            imageRectX = transform?.ImageRectangle.X,
            imageRectY = transform?.ImageRectangle.Y,
            imageRectWidth = transform?.ImageRectangle.Width,
            imageRectHeight = transform?.ImageRectangle.Height,
            zoom = viewport.Zoom,
            panX = viewport.Pan.X,
            panY = viewport.Pan.Y,
            processId = Environment.ProcessId,
            legacyKva = legacyKvaDocument is { } legacy ? new
            {
                path = legacy.SourcePath,
                formatVersion = legacy.FormatVersion,
                keyframeCount = legacy.KeyframeCount,
                trackCount = legacy.TrackCount,
                chronoCount = legacy.ChronoCount,
                recognizedDrawingCount = legacy.RecognizedDrawingCount,
                sectionCounts = legacy.SectionCounts
            } : null,
            legacyKvaSavePath = lastLegacyKvaSavePath,
            analysisWorkspacePath = lastAnalysisPath,
            analysisTimestamp,
            analysis = distanceAnalysis,
            calibrationStart,
            calibrationEnd,
            collectingCalibration,
            collectingMeasurement,
            calibrateButton = ControlBounds(calibrateButton),
            measureButton = ControlBounds(measureButton),
            saveAnalysisButton = ControlBounds(saveAnalysisButton),
            openAnalysisButton = ControlBounds(openAnalysisButton),
            saveLegacyKvaButton = ControlBounds(saveLegacyKvaButton)
        };
        string fullPath = Path.GetFullPath(smokeReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private object? ControlBounds(Control? control)
    {
        if (control is null) return null;
        Point? origin = control.TranslatePoint(new Point(0, 0), this);
        return new
        {
            x = origin?.X,
            y = origin?.Y,
            width = control.Bounds.Width,
            height = control.Bounds.Height
        };
    }

    private string? GetCurrentInputHash()
    {
        string? path = session.CurrentPath;
        return HashFile(path);
    }

    private static string? HashFile(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
