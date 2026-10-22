using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kinovea.PortableDesktop.Presentation;
using Kinovea.PortableVideo;

namespace Kinovea.PortableDesktop;

/// <summary>Two independent players with shared seek, step and play controls.</summary>
internal sealed class DualPlayerWindow : Window
{
    private readonly MediaSlot[] slots = [new(), new()];
    private readonly Slider commonTimeline = new() { Minimum = 0, Maximum = 1000, SmallChange = 1, LargeChange = 25, IsEnabled = false };
    private readonly TextBlock status = new() { Text = "Open one or two media files to compare." };
    private readonly CheckBox linked = new() { Content = "Link navigation", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button play = new() { Content = "Play", IsEnabled = false };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<Control> playerHosts = [];
    private Grid? playerGrid;
    private DualPlayerTimeline? timeline;
    private bool updatingSlider;
    private bool tickRunning;
    private bool verticalLayout;

    public DualPlayerWindow()
    {
        Title = "Kinovea · Compare media";
        Width = 1280;
        Height = 760;
        MinWidth = 800;
        MinHeight = 540;
        Background = Brush("#20252c");
        Content = BuildLayout();
        for (int i = 0; i < slots.Length; i++)
        {
            DragDrop.SetAllowDrop(slots[i].Viewport, true);
            DragDrop.AddDragOverHandler(slots[i].Viewport, (_, e) =>
            {
                if (e.DataTransfer.TryGetFiles()?.Length > 0)
                    e.DragEffects = DragDropEffects.Copy;
            });
            DragDrop.AddDropHandler(slots[i].Viewport, (_, e) =>
            {
                IStorageItem? item = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
                if (item is not IStorageFile file || file.TryGetLocalPath() is not { } path) return;
                e.Handled = true;
                int slotIndex = Array.FindIndex(slots, slot => ReferenceEquals(slot.Viewport, slots[i].Viewport));
                if (slotIndex < 0) return;
                _ = OpenPathAsync(slotIndex, path);
            });
            slots[i].Changed += SlotChanged;
        }
        commonTimeline.ValueChanged += (_, _) =>
        {
            if (updatingSlider || timeline is null) return;
            _ = SeekCommonAsync(timeline.FromFraction(commonTimeline.Value / 1000d));
        };
        timer.Tick += Tick;
        Closed += async (_, _) => await DisposeSlotsAsync();
    }

    private Control BuildLayout()
    {
        var openFirst = new Button { Content = "Open left…", Margin = new Thickness(4) };
        var openSecond = new Button { Content = "Open right…", Margin = new Thickness(4) };
        var previous = new Button { Content = "◀", Margin = new Thickness(4) };
        var next = new Button { Content = "▶", Margin = new Thickness(4) };
        var swap = new Button { Content = "Swap", Margin = new Thickness(4) };
        var orientation = new Button { Content = "Stack vertically", Margin = new Thickness(4) };
        openFirst.Click += async (_, _) => await PickAndOpenAsync(0);
        openSecond.Click += async (_, _) => await PickAndOpenAsync(1);
        previous.Click += async (_, _) => await StepAsync(backward: true);
        next.Click += async (_, _) => await StepAsync(backward: false);
        play.Click += (_, _) => TogglePlay();
        swap.Click += (_, _) => SwapSlots();
        orientation.Click += (_, _) =>
        {
            verticalLayout = !verticalLayout;
            orientation.Content = verticalLayout ? "Place side by side" : "Stack vertically";
            UpdatePlayerGrid();
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (Control control in new Control[] { openFirst, openSecond, previous, play, next, commonTimeline, linked, swap, orientation })
            toolbar.Children.Add(control);

        playerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*") };
        for (int i = 0; i < slots.Length; i++)
        {
            playerHosts.Add(CreatePlayerHost(i));
            playerGrid.Children.Add(playerHosts[i]);
        }
        UpdatePlayerGrid();

        var root = new DockPanel();
        var toolbarBorder = new Border { Background = Brush("#2b323b"), Child = toolbar, Padding = new Thickness(6) };
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        root.Children.Add(toolbarBorder);
        status.Margin = new Thickness(10, 5);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(playerGrid);
        return root;
    }

    private Control CreatePlayerHost(int index)
    {
        MediaSlot slot = slots[index];
        slot.Viewport.ImageRendered += RefreshStatus;
        var title = new TextBlock { Text = index == 0 ? "Left player" : "Right player", Margin = new Thickness(10, 6), FontSize = 16 };
        var canvas = new Grid();
        canvas.Children.Add(slot.Viewport);
        var hint = new TextBlock
        {
            Text = "Open a media file",
            Foreground = Brush("#9aa4b2"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        slot.EmptyHint = hint;
        canvas.Children.Add(hint);
        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(canvas);
        return new Border { Margin = new Thickness(4), BorderBrush = Brush("#424a55"), BorderThickness = new Thickness(1), Child = panel };
    }

    private async Task PickAndOpenAsync(int index)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = index == 0 ? "Open left media" : "Open right media",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Supported media")
                {
                    Patterns = ["*.bmp", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v"]
                },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            await OpenPathAsync(index, path);
    }

    private async Task OpenPathAsync(int index, string path)
    {
        bool opened = await slots[index].OpenAsync(path);
        if (!opened)
        {
            status.Text = slots[index].ErrorMessage ?? $"Could not open {Path.GetFileName(path)}.";
            return;
        }
        RebuildTimeline();
        RefreshStatus();
    }

    private void SlotChanged(object? sender, EventArgs e)
    {
        if (sender is not MediaSlot slot) return;
        if (Dispatcher.UIThread.CheckAccess())
        {
            slot.RefreshView();
            RebuildTimeline();
            RefreshStatus();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                slot.RefreshView();
                RebuildTimeline();
                RefreshStatus();
            });
        }
    }

    private void RebuildTimeline()
    {
        if (slots[0].Clock is { } first && slots[1].Clock is { } second)
        {
            timeline = new DualPlayerTimeline(first, second);
            commonTimeline.IsEnabled = true;
            double common = linked.IsChecked == true
                ? timeline.ToCommonSeconds(0, slots[0].Timestamp)
                : timeline.StartSeconds;
            updatingSlider = true;
            commonTimeline.Value = timeline.ToFraction(common) * 1000;
            updatingSlider = false;
        }
        else
        {
            timeline = null;
            commonTimeline.IsEnabled = false;
        }
        play.IsEnabled = slots.Any(slot => slot.IsVideo);
    }

    private async Task SeekCommonAsync(double commonSeconds)
    {
        if (timeline is null) return;
        await slots[0].SeekAsync(timeline.ToLocalTimestamp(0, commonSeconds));
        if (linked.IsChecked == true)
            await slots[1].SeekAsync(timeline.ToLocalTimestamp(1, commonSeconds));
        RefreshStatus();
    }

    private async Task StepAsync(bool backward)
    {
        await slots[0].StepAsync(backward);
        if (linked.IsChecked == true)
            await slots[1].StepAsync(backward);
        RebuildTimeline();
    }

    private void TogglePlay()
    {
        bool anyPlaying = slots.Any(slot => slot.IsPlaying);
        foreach (MediaSlot slot in slots)
        {
            if (!slot.IsVideo) continue;
            if (anyPlaying) slot.Pause();
            else slot.Play();
        }
        play.Content = anyPlaying ? "Play" : "Pause";
        if (anyPlaying) timer.Stop();
        else timer.Start();
        RefreshStatus();
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (tickRunning) return;
        tickRunning = true;
        try
        {
            foreach (MediaSlot slot in slots.Where(slot => slot.IsVideo && slot.IsPlaying))
                await slot.PlaybackTickAsync();
            if (!slots.Any(slot => slot.IsPlaying))
            {
                timer.Stop();
                play.Content = "Play";
            }
            RebuildTimeline();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            foreach (MediaSlot slot in slots) slot.Pause();
            timer.Stop();
            play.Content = "Play";
            status.Text = $"Comparison playback stopped ({exception.GetType().Name}).";
        }
        finally { tickRunning = false; }
    }

    private void SwapSlots()
    {
        (slots[0], slots[1]) = (slots[1], slots[0]);
        (playerHosts[0], playerHosts[1]) = (playerHosts[1], playerHosts[0]);
        UpdatePlayerGrid();
        RebuildTimeline();
        RefreshStatus();
    }

    private void UpdatePlayerGrid()
    {
        if (playerGrid is null || playerHosts.Count != 2) return;
        playerGrid.Children.Clear();
        playerGrid.RowDefinitions.Clear();
        playerGrid.ColumnDefinitions.Clear();
        if (verticalLayout)
        {
            playerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            playerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            playerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(playerHosts[0], 0); Grid.SetColumn(playerHosts[0], 0);
            Grid.SetRow(playerHosts[1], 1); Grid.SetColumn(playerHosts[1], 0);
        }
        else
        {
            playerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            playerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            playerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(playerHosts[0], 0); Grid.SetColumn(playerHosts[0], 0);
            Grid.SetRow(playerHosts[1], 0); Grid.SetColumn(playerHosts[1], 1);
        }
        playerGrid.Children.Add(playerHosts[0]);
        playerGrid.Children.Add(playerHosts[1]);
    }

    private void RefreshStatus()
    {
        status.Text = $"Left: {slots[0].Label} · Right: {slots[1].Label}";
        play.Content = slots.Any(slot => slot.IsPlaying) ? "Pause" : "Play";
    }

    private async Task DisposeSlotsAsync()
    {
        timer.Stop();
        foreach (MediaSlot slot in slots)
            await slot.DisposeAsync();
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private sealed class MediaSlot : IAsyncDisposable
    {
        private readonly PortablePlayerSession image = new();
        private readonly PortableVideoPlayerSession video = new();
        private bool isVideo;

        public event EventHandler? Changed;
        public ImageViewport Viewport { get; } = new();
        public TextBlock? EmptyHint { get; set; }
        public bool IsVideo => isVideo && video.Status == PortablePlayerStatus.Open;
        public bool IsPlaying => IsVideo && video.IsPlaying;
        public string Label => IsVideo ? $"{Path.GetFileName(video.CurrentPath)} · {video.CurrentTimestamp}" :
            image.Status == PortablePlayerStatus.Open ? $"{Path.GetFileName(image.CurrentPath)} · {image.Current.Timestamp}" : "empty";
        public string? ErrorMessage => IsVideo ? video.ErrorMessage : image.ErrorMessage;
        public long Timestamp => IsVideo ? video.CurrentTimestamp : image.Current.Timestamp;

        public MediaClock? Clock
        {
            get
            {
                if (IsVideo && video.Metadata is { } videoInfo)
                    return new MediaClock(video.TimelineStart, video.TimelineEnd, videoInfo.TimeBaseNumerator, videoInfo.TimeBaseDenominator);
                if (!isVideo && image.Status == PortablePlayerStatus.Open && image.Metadata is { } imageInfo)
                {
                    int fps = Math.Max(1, (int)Math.Round(imageInfo.FramesPerSecond));
                    return new MediaClock(image.TimelineStart, image.TimelineEnd, 1, fps);
                }
                return null;
            }
        }

        public MediaSlot()
        {
            image.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            video.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        public async Task<bool> OpenAsync(string path)
        {
            if (IsVideoPath(path))
            {
                bool opened = await video.OpenAsync(path);
                if (opened)
                {
                    image.Close();
                    isVideo = true;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                return opened;
            }

            bool imageOpened = image.Open(path);
            if (imageOpened)
            {
                if (isVideo) await video.CloseAsync();
                isVideo = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            return imageOpened;
        }

        public void RefreshView()
        {
            Viewport.SetBitmap(IsVideo ? video.PresentationBitmap : image.PresentationBitmap);
            if (EmptyHint is not null)
                EmptyHint.IsVisible = IsVideo ? video.Status != PortablePlayerStatus.Open : image.Status != PortablePlayerStatus.Open;
        }

        public async Task SeekAsync(long timestamp)
        {
            if (IsVideo) await video.SeekAsync(timestamp);
            else if (!isVideo) image.Seek(timestamp);
        }

        public async Task StepAsync(bool backward)
        {
            if (IsVideo)
            {
                video.Pause();
                if (backward) await video.StepBackwardAsync(); else await video.StepForwardAsync();
            }
            else if (image.Status == PortablePlayerStatus.Open)
            {
                if (backward) image.StepBackward(); else image.StepForward();
            }
        }

        public void Play() { if (IsVideo) video.Play(); }
        public void Pause() { if (IsVideo) video.Pause(); }
        public Task<bool> PlaybackTickAsync() => video.PlaybackTickAsync();

        public async ValueTask DisposeAsync()
        {
            image.Dispose();
            await video.DisposeAsync();
        }

        private static bool IsVideoPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v" or ".gif";
    }
}
