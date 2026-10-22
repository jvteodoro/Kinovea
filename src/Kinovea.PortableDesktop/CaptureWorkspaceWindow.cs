using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kinovea.PortableDesktop.Presentation;

namespace Kinovea.PortableDesktop;

internal sealed class CaptureWorkspaceWindow : Window
{
    public CaptureWorkspaceWindow(CaptureLayout layout, int frameIntervalMilliseconds, string uiCulture)
    {
        bool dual = layout == CaptureLayout.Dual;
        Title = new PortableDesktopStrings(uiCulture).CaptureWindowTitle(dual);
        Width = dual ? 1100 : 650;
        Height = 520;
        MinWidth = dual ? 800 : 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var sourceProvider = new SyntheticCaptureSourceProvider();
        var panels = new List<CaptureSourcePanel>
        {
            new(sourceProvider, frameIntervalMilliseconds, 0, uiCulture)
        };
        if (dual)
            panels.Add(new CaptureSourcePanel(sourceProvider, frameIntervalMilliseconds, 1, uiCulture));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(12) };
        foreach (CaptureSourcePanel panel in panels)
        {
            panel.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Width = dual ? 520 : 610;
            row.Children.Add(panel);
        }
        Content = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Closed += async (_, _) =>
        {
            foreach (CaptureSourcePanel panel in panels)
                await panel.DisposeAsync();
        };
    }
}

internal sealed class CaptureSourcePanel : Border, IAsyncDisposable
{
    private readonly IPortableCaptureSourceProvider provider;
    private readonly int intervalMilliseconds;
    private readonly int defaultSourceIndex;
    private readonly PortableDesktopStrings strings;
    private readonly ComboBox sourceSelector;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Image image;
    private readonly TextBlock status;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private IPortableCaptureSource? source;
    private WriteableBitmap? bitmap;
    private bool readPending;
    private bool disposed;

    public CaptureSourcePanel(IPortableCaptureSourceProvider provider, int intervalMilliseconds, int defaultSourceIndex, string uiCulture)
    {
        this.provider = provider;
        this.intervalMilliseconds = intervalMilliseconds;
        this.defaultSourceIndex = defaultSourceIndex;
        strings = new PortableDesktopStrings(uiCulture);
        sourceSelector = new ComboBox { ItemsSource = provider.SourceNames, SelectedIndex = Math.Min(defaultSourceIndex, provider.SourceNames.Count - 1), MinWidth = 190 };
        startButton = new Button { Content = strings.Start };
        stopButton = new Button { Content = strings.Stop, IsEnabled = false };
        image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        status = new TextBlock { Text = strings.Stopped, Foreground = Brushes.LightGray };
        startButton.Click += async (_, _) => await StartAsync();
        stopButton.Click += async (_, _) => await StopAsync();
        timer.Tick += TimerTick;

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        controls.Children.Add(sourceSelector);
        controls.Children.Add(startButton);
        controls.Children.Add(stopButton);
        var layout = new DockPanel();
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        layout.Children.Add(controls);
        layout.Children.Add(status);
        layout.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#111820")), Child = image, MinHeight = 280 });
        Child = layout;
        Padding = new Thickness(10);
        Background = new SolidColorBrush(Color.Parse("#252b33"));
        CornerRadius = new CornerRadius(4);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        if (source is not null)
        {
            await source.StopAsync();
            await source.DisposeAsync();
            source = null;
        }
        image.Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }

    private async Task StartAsync()
    {
        if (disposed) return;
        await StopSourceAsync();
        string name = sourceSelector.SelectedItem as string
            ?? provider.SourceNames[Math.Min(defaultSourceIndex, provider.SourceNames.Count - 1)];
        source = provider.Create(name, TimeSpan.FromMilliseconds(intervalMilliseconds));
        await source.StartAsync();
        sourceSelector.IsEnabled = false;
        startButton.IsEnabled = false;
        stopButton.IsEnabled = true;
        status.Text = $"{source.DisplayName} · {strings.WaitingForFrames}";
        timer.Start();
    }

    private async Task StopAsync()
    {
        timer.Stop();
        readPending = false;
        await StopSourceAsync();
        sourceSelector.IsEnabled = true;
        startButton.IsEnabled = true;
        stopButton.IsEnabled = false;
        status.Text = strings.Stopped;
    }

    private async Task StopSourceAsync()
    {
        if (source is null) return;
        await source.StopAsync();
        await source.DisposeAsync();
        source = null;
    }

    private async void TimerTick(object? sender, EventArgs e)
    {
        if (disposed || readPending || source is null) return;
        readPending = true;
        try
        {
            IPortableCaptureSource activeSource = source;
            using PortableCaptureFrame frame = await activeSource.ReadFrameAsync();
            if (disposed || !ReferenceEquals(source, activeSource)) return;
            PixelBitmapResult converted = PixelBufferBitmapAdapter.Create(frame);
            bitmap?.Dispose();
            bitmap = converted.Bitmap;
            image.Source = bitmap;
            status.Text = $"{source.DisplayName} · {strings.Frame(frame.Sequence, frame.Width, frame.Height)}";
        }
        catch (OperationCanceledException) when (disposed || source is null || !source.IsRunning)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            timer.Stop();
            await StopSourceAsync();
            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;
            sourceSelector.IsEnabled = true;
            status.Text = strings.CaptureStopped(exception.GetType().Name);
        }
        finally
        {
            readPending = false;
        }
    }
}
