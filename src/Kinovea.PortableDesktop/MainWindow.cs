using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
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
    private Button? openButton;
    private Button? closeButton;
    private Button? fitButton;
    private Button? playPauseButton;

    private Button? browseButton;
    private Button? compareButton;

    private Button? loopButton;
    private Button? speedDownButton;
    private Button? speedUpButton;
    private Button? jumpBackButton;
    private Button? jumpForwardButton;
    private TextBlock? speedLabel;
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
    private readonly PortableApplicationState appState;
    private readonly PortableApplicationStateStore stateStore;

    public MainWindow(string[] args, PortableApplicationState appState, PortableApplicationStateStore stateStore)
    {
        this.appState = appState;
        this.stateStore = stateStore;
        Title = "Kinovea Native Player";
        Width = appState.WindowWidth;
        Height = appState.WindowHeight;
        MinWidth = 720;
        MinHeight = 480;
        Background = Brush(appState.Theme == "Light" ? "#eef1f5" : "#20252c");
        initialPath = Argument(args, "--open");
        initialVideoPath = Argument(args, "--open-video");
        smokeReportPath = Argument(args, "--smoke-report");
        Content = BuildLayout();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) =>
        {
            if (e.DataTransfer.TryGetFiles()?.Length > 0)
                e.DragEffects = DragDropEffects.Copy;
        });
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            IStorageItem? item = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
            string? path = (item as IStorageFile)?.TryGetLocalPath();
            if (path is null) return;
            e.Handled = true;
            if (Directory.Exists(path))
            {
                _ = BrowseAtPathAsync(path);
                return;
            }
            _ = OpenPathAsync(path);
        });

        session.Changed += SessionChanged;
        videoSession.Changed += VideoSessionChanged;
        videoTimer.Tick += VideoTimerTick;
        viewport.ImageRendered += () =>
        {
            visibleFrameRendered = true;
            WriteSmokeReport();
        };
        viewport.InteractionObserved += WriteSmokeReport;
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
        AddHandler(KeyDownEvent, WindowKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => session.Dispose();
        Closed += async (_, _) => await videoSession.DisposeAsync();
        Closed += (_, _) => SaveApplicationState();
        SizeChanged += (_, _) =>
        {
            appState.WindowWidth = Math.Clamp(Width, 720, 16384);
            appState.WindowHeight = Math.Clamp(Height, 480, 16384);
        };
        Refresh();
    }

    private Control BuildLayout()
    {
        var open = openButton = new Button { Content = "Open image…", Margin = new Thickness(4) };
        var browse = browseButton = new Button { Content = "Browse media…", Margin = new Thickness(4) };
        var compare = compareButton = new Button { Content = "Compare two…", Margin = new Thickness(4) };
        var close = closeButton = new Button { Content = "Close", Margin = new Thickness(4), IsEnabled = false };
        var play = playPauseButton = new Button { Content = "Play", Margin = new Thickness(4), IsEnabled = false };
        var fit = fitButton = new Button { Content = "Fit", Margin = new Thickness(4) };
        var zoomOut = new Button { Content = "−", Margin = new Thickness(4) };
        var zoomIn = new Button { Content = "+", Margin = new Thickness(4) };
        var zoomLabel = new TextBlock { Text = "Fit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        var decreaseSpeed = speedDownButton = new Button { Content = "− speed", Margin = new Thickness(4), IsEnabled = false };
        var increaseSpeed = speedUpButton = new Button { Content = "+ speed", Margin = new Thickness(4), IsEnabled = false };
        var speed = speedLabel = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center };
        var loop = loopButton = new Button { Content = "Loop: On", Margin = new Thickness(4), IsEnabled = false };
        var jumpBack = jumpBackButton = new Button { Content = "−0.5 s", Margin = new Thickness(4), IsEnabled = false };
        var jumpForward = jumpForwardButton = new Button { Content = "+0.5 s", Margin = new Thickness(4), IsEnabled = false };
        viewport.ZoomChanged += zoom =>
        {
            zoomLabel.Text = zoom == 1 ? "Fit" : $"{zoom:P0}";
            WriteSmokeReport();
        };
        var emptyHint = emptyHintText = new TextBlock
        {
            Text = "Open a BMP, PNG, JPEG, GIF, or video file",
            Foreground = Brush("#9aa4b2"),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        open.Click += async (_, _) => await PickAndOpenAsync();
        browse.Click += async (_, _) => await BrowseAndOpenAsync();
        compare.Click += (_, _) => new DualPlayerWindow().Show(this);
        close.Click += async (_, _) => await CloseActiveAsync();
        play.Click += (_, _) => TogglePlayback();
        decreaseSpeed.Click += (_, _) => ChangePlaybackSpeed(large: true, increase: false);
        increaseSpeed.Click += (_, _) => ChangePlaybackSpeed(large: true, increase: true);
        loop.Click += (_, _) =>
        {
            loop.Tag = !IsLoopPlaybackEnabled;
            loop.Content = IsLoopPlaybackEnabled ? "Loop: On" : "Loop: Off";
            WriteSmokeReport();
        };
        jumpBack.Click += async (_, _) => await JumpBySecondsAsync(-0.5);
        jumpForward.Click += async (_, _) => await JumpBySecondsAsync(0.5);
        fit.Click += (_, _) => { viewport.Fit(); zoomLabel.Text = "Fit"; };
        zoomOut.Click += (_, _) => { viewport.ChangeZoom(viewport.Zoom / 1.2); zoomLabel.Text = $"{viewport.Zoom:P0}"; };
        zoomIn.Click += (_, _) => { viewport.ChangeZoom(viewport.Zoom * 1.2); zoomLabel.Text = $"{viewport.Zoom:P0}"; };
        previous.Click += async (_, _) => await StepBackwardAsync();
        next.Click += async (_, _) => await StepForwardAsync();
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
        toolbar.Children.Add(browse);
        toolbar.Children.Add(compare);
        toolbar.Children.Add(timeline);
        toolbar.Children.Add(fit);
        toolbar.Children.Add(zoomOut);
        toolbar.Children.Add(zoomIn);
        toolbar.Children.Add(zoomLabel);

        var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        transport.Children.Add(decreaseSpeed);
        transport.Children.Add(speed);
        transport.Children.Add(increaseSpeed);
        transport.Children.Add(loop);
        transport.Children.Add(new Separator { Margin = new Thickness(8, 4) });
        transport.Children.Add(jumpBack);
        transport.Children.Add(jumpForward);

        var header = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 8) };
        header.Children.Add(name);
        header.Children.Add(metadata);
        header.Children.Add(status);

        var top = new StackPanel { Orientation = Orientation.Vertical };
        var menu = new PortableDesktopMenu(appState);
        menu.OpenImageRequested += async (_, _) => await PickAndOpenAsync();
        menu.CloseMediaRequested += async (_, _) => await CloseActiveAsync();
        menu.PreferencesRequested += async (_, _) => await ShowPreferencesAsync();
        menu.CaptureRequested += (_, layout) => new CaptureWorkspaceWindow(layout, appState.SyntheticCaptureIntervalMilliseconds, appState.UiCulture).Show(this);
        menu.SaveWorkspaceRequested += (_, _) => SaveApplicationState();
        top.Children.Add(menu);
        var toolbarBorder = new Border { Background = Brush("#2b323b"), Child = toolbar, Padding = new Thickness(6) };
        top.Children.Add(toolbarBorder);
        top.Children.Add(new Border { Background = Brush("#2b323b"), Child = transport, Padding = new Thickness(6, 0) });
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
                new FilePickerFileType("Images and video") { Patterns = ["*.bmp", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v"] },
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

    private async Task BrowseAndOpenAsync()
    {
        string? initialDirectory = session.CurrentPath is { } path
            ? Path.GetDirectoryName(path)
            : null;
        await BrowseAtPathAsync(initialDirectory);
    }

    private async Task BrowseAtPathAsync(string? initialDirectory)
    {
        var browser = new FileBrowserWindow(initialDirectory);
        string? selectedPath = await browser.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(selectedPath))
            await OpenPathAsync(selectedPath);
    }

    private async Task OpenPathAsync(string path)
    {
        lastAttemptedFileName = Path.GetFileName(path);
        bool opened;
        if (IsVideoPath(path))
        {
            opened = await videoSession.OpenAsync(path);
            if (opened)
            {
                if (session.Status == PortablePlayerStatus.Open)
                    session.Close();
                activeVideo = true;
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
                viewport.SetBitmap(session.PresentationBitmap);
            }
        }
        lastOpenResult = opened ? "Opened" : "Rejected";
        if (opened)
        {
            appState.LastOpenPath = Path.GetFullPath(path);
            appState.LastOpenDirectory = Path.GetDirectoryName(appState.LastOpenPath);
            visibleFrameRendered = false;
            SaveApplicationState();
        }
        if (!opened)
            WriteSmokeReport();
        Refresh();
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
            PortableVideoMetadata? video = videoSession.Metadata;
            bool videoLoaded = videoSession.Status == PortablePlayerStatus.Open && video is not null;
            name.Text = videoLoaded ? Path.GetFileNameWithoutExtension(videoSession.CurrentPath) : "Kinovea";
            metadata.Text = videoLoaded
                ? $"{video!.Width} × {video.Height} px    {video.AverageFrameRate:0.00} fps    PTS {videoSession.CurrentTimestamp} / {videoSession.TimelineEnd}"
                : string.Empty;
            status.Text = videoSession.ErrorMessage ?? (videoLoaded ? (videoSession.IsPlaying ? "Video · playing" : "Video · paused") : "Open a supported image or video file to begin.");
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
            if (speedDownButton is not null) speedDownButton.IsEnabled = videoLoaded;
            if (speedUpButton is not null) speedUpButton.IsEnabled = videoLoaded;
            if (loopButton is not null) loopButton.IsEnabled = videoLoaded;
            if (jumpBackButton is not null) jumpBackButton.IsEnabled = videoLoaded;
            if (jumpForwardButton is not null) jumpForwardButton.IsEnabled = videoLoaded;
            if (speedLabel is not null) speedLabel.Text = $"{videoSession.PlaybackRate:P0}";
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
        if (speedDownButton is not null) speedDownButton.IsEnabled = false;
        if (speedUpButton is not null) speedUpButton.IsEnabled = false;
        if (loopButton is not null) loopButton.IsEnabled = false;
        if (jumpBackButton is not null) jumpBackButton.IsEnabled = false;
        if (jumpForwardButton is not null) jumpForwardButton.IsEnabled = false;
        viewport.InvalidateVisual();
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Shift) == (appState.OpenShortcut == "Ctrl+Shift+O"))
        {
            _ = PickAndOpenAsync();
            e.Handled = true;
        }

        else if (control && e.Key == Key.OemComma)
        {
            _ = ShowPreferencesAsync();
            e.Handled = true;
        }
        else if ((control && e.Key == Key.W) || e.Key == Key.Escape)

        else if (control && (e.Key == Key.W || e.Key == Key.F4))
        {
            _ = CloseActiveAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewport.Fit();
            e.Handled = true;
        }
        else if (activeVideo && (e.Key == Key.Up || e.Key == Key.Down))
        {
            ChangePlaybackSpeed(large: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), increase: e.Key == Key.Up);
            e.Handled = true;
        }
        else if (activeVideo && (e.Key == Key.PageUp || e.Key == Key.PageDown))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _ = JumpBySecondsAsync(e.Key == Key.PageDown ? 0.5 : -0.5);
            else
                _ = JumpByTimelinePercentAsync(e.Key == Key.PageDown);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && activeVideo && videoSession.CurrentTimestamp <= videoSession.TimelineStart)
        {
            _ = videoSession.SeekAsync(videoSession.TimelineEnd);
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

    private async Task ShowPreferencesAsync()
    {
        var dialog = new PreferencesWindow(appState);
        bool? saved = await dialog.ShowDialog<bool?>(this);
        if (saved != true) return;
        Application.Current!.RequestedThemeVariant = appState.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        Background = Brush(appState.Theme == "Light" ? "#eef1f5" : "#20252c");
        SaveApplicationState();
    }

    private void SaveApplicationState()
    {
        try { stateStore.Save(appState); }
        catch (IOException exception) { status.Text = $"Could not save preferences ({exception.GetType().Name})."; }
        catch (UnauthorizedAccessException) { status.Text = "Could not save preferences (access denied)."; }
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
            viewport.SetBitmap(session.PresentationBitmap);
        }
        else
            session.Close();
        Refresh();
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

    private async void VideoTimerTick(object? sender, EventArgs e)
    {
        if (!activeVideo || videoTickRunning) return;
        videoTickRunning = true;
        try
        {
            await videoSession.PlaybackTickAsync(IsLoopPlaybackEnabled);
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

    private bool IsLoopPlaybackEnabled => loopButton?.Tag is not bool enabled || enabled;

    private void ChangePlaybackSpeed(bool large, bool increase)
    {
        if (!activeVideo || videoSession.Status != PortablePlayerStatus.Open) return;
        videoSession.SetPlaybackRate(StepPlaybackRate(videoSession.PlaybackRate, large, increase));
        WriteSmokeReport();
    }

    private static double StepPlaybackRate(double current, bool large, bool increase)
    {
        bool belowNormal = current < 1.0 || current == 1.0 && !increase;
        double minimum = belowNormal ? 0.0 : 1.0;
        double maximum = belowNormal ? 1.0 : 10.0;
        double increment = belowNormal ? (large ? 0.1 : 0.01) : (large ? 1.0 : 0.1);
        double index = (current - minimum) / increment;
        double nearest = Math.Round(index);
        if (Math.Abs(index - nearest) < 1e-3) index = nearest;
        double next = increase ? Math.Floor(index) + 1 : Math.Ceiling(index) - 1;
        next = Math.Clamp(next, 0, (maximum - minimum) / increment);
        return Math.Max(0.01, Math.Round(minimum + next * increment, 3));
    }

    private async Task JumpBySecondsAsync(double seconds)
    {
        if (!activeVideo || videoSession.Metadata is not { } metadata) return;
        long delta = (long)Math.Round(seconds * metadata.TimeBaseDenominator / metadata.TimeBaseNumerator);
        long target = SaturatingAdd(videoSession.CurrentTimestamp, delta);
        await videoSession.SeekAsync(Math.Clamp(target, videoSession.TimelineStart, videoSession.TimelineEnd));
    }

    private async Task JumpByTimelinePercentAsync(bool forward)
    {
        if (!activeVideo || videoSession.Metadata is not { } metadata) return;
        decimal frameDuration = (decimal)metadata.TimeBaseDenominator /
            metadata.TimeBaseNumerator / (decimal)Math.Max(metadata.AverageFrameRate, 1e-9);
        decimal range = Math.Max(1, videoSession.TimelineEnd - videoSession.TimelineStart + frameDuration);
        decimal step = range / 10m;
        decimal index = (videoSession.CurrentTimestamp - videoSession.TimelineStart) / range * 10m;
        decimal nearest = Math.Round(index);
        decimal frameTolerance = frameDuration / range * 10m;
        if (Math.Abs(index - nearest) < frameTolerance) index = nearest;
        decimal next = forward ? decimal.Floor(index) + 1 : decimal.Ceiling(index) - 1;
        next = Math.Clamp(next, 0m, 10m);
        long target = (long)Math.Round(videoSession.TimelineStart + next * step);
        await videoSession.SeekAsync(Math.Clamp(target, videoSession.TimelineStart, videoSession.TimelineEnd));
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue :
        right < 0 && left < long.MinValue - right ? long.MinValue : left + right;

    private static bool IsVideoPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v" or ".gif";

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
            previousButton = ControlBounds(previous),
            nextButton = ControlBounds(next),
            browseButton = ControlBounds(browseButton),
            compareButton = ControlBounds(compareButton),
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
        Size clientSize = ClientSize;
        var report = new
        {
            schemaVersion = 1,
            mediaType = "video",
            status = visibleFrameRendered && videoSession.Status == PortablePlayerStatus.Open ? "VISIBLE_FRAME_RENDERED" : videoSession.Status.ToString().ToUpperInvariant(),
            sourceSha = Environment.GetEnvironmentVariable("KINOVEA_SOURCE_SHA") ?? "LOCAL_UNRECORDED",
            fileName = videoSession.CurrentPath is null ? null : Path.GetFileName(videoSession.CurrentPath),
            inputSha256 = HashFile(videoSession.CurrentPath),
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
            playbackRate = videoSession.PlaybackRate,
            loopPlayback = IsLoopPlaybackEnabled,
            presentationCopyCount = videoSession.PresentationCopies,
            presentationBytesCopied = videoSession.PresentationBytesCopied,
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
            playPauseButton = ControlBounds(playPauseButton),
            loopButton = ControlBounds(loopButton),
            speedDownButton = ControlBounds(speedDownButton),
            speedUpButton = ControlBounds(speedUpButton),
            jumpBackButton = ControlBounds(jumpBackButton),
            jumpForwardButton = ControlBounds(jumpForwardButton),
            processId = Environment.ProcessId
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
