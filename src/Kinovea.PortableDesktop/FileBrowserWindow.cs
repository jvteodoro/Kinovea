using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Kinovea.PortableDesktop;

/// <summary>Portable filesystem navigation and media thumbnail picker.</summary>
internal sealed class FileBrowserWindow : Window
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".bmp", ".png", ".jpg", ".jpeg" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".gif" };

    private readonly TextBox location = new();
    private readonly ComboBox filter = new();
    private readonly ComboBox sort = new();
    private readonly WrapPanel tiles = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TreeView folders = new();
    private readonly Button openButton = new() { Content = "Open", IsEnabled = false };
    private readonly List<Bitmap> thumbnailBitmaps = [];
    private readonly string? requestedPath;
    private string? selectedPath;
    private bool descending;

    public FileBrowserWindow(string? initialPath)
    {
        requestedPath = initialPath;
        Title = "Browse media";
        Width = 1040;
        Height = 680;
        MinWidth = 700;
        MinHeight = 480;
        Background = Brush("#20252c");
        Content = BuildLayout();
        Opened += (_, _) => Initialize();
    }

    private Control BuildLayout()
    {
        location.HorizontalAlignment = HorizontalAlignment.Stretch;
        location.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Navigate(location.Text);
                e.Handled = true;
            }
        };

        var go = new Button { Content = "Go", Margin = new Thickness(4, 0) };
        go.Click += (_, _) => Navigate(location.Text);
        var home = new Button { Content = "Home", Margin = new Thickness(4, 0) };
        home.Click += (_, _) => Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var up = new Button { Content = "Up", Margin = new Thickness(4, 0) };
        up.Click += (_, _) => Navigate(Directory.GetParent(location.Text ?? string.Empty)?.FullName ?? location.Text);
        var direction = new Button { Content = "Ascending", Margin = new Thickness(4, 0) };
        direction.Click += (_, _) =>
        {
            descending = !descending;
            direction.Content = descending ? "Descending" : "Ascending";
            LoadItems();
        };
        filter.Items.Add("Supported media");
        filter.Items.Add("Images");
        filter.Items.Add("Video");
        filter.SelectedIndex = 0;
        filter.SelectionChanged += (_, _) => LoadItems();
        sort.Items.Add("Name");
        sort.Items.Add("Date modified");
        sort.Items.Add("Size");
        sort.SelectedIndex = 0;
        sort.SelectionChanged += (_, _) => LoadItems();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        toolbar.Children.Add(home);
        toolbar.Children.Add(up);
        toolbar.Children.Add(location);
        toolbar.Children.Add(go);
        toolbar.Children.Add(filter);
        toolbar.Children.Add(sort);
        toolbar.Children.Add(direction);

        folders.MinWidth = 210;
        folders.SelectionChanged += (_, _) =>
        {
            if (folders.SelectedItem is TreeViewItem { Tag: string path })
                Navigate(path);
        };

        var mediaScroll = new ScrollViewer { Content = tiles, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(4) };
        right.Children.Add(mediaScroll);
        var details = new Border
        {
            Background = Brush("#252b33"),
            Padding = new Thickness(10),
            Child = preview
        };
        Grid.SetRow(details, 1);
        right.Children.Add(details);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("220,4,*"), Margin = new Thickness(6, 0, 6, 6) };
        body.Children.Add(folders);
        var separator = new Border { Background = Brush("#3b424c") };
        Grid.SetColumn(separator, 1);
        body.Children.Add(separator);
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        openButton.Margin = new Thickness(4);
        openButton.Click += (_, _) => Close(selectedPath);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(4) };
        cancel.Click += (_, _) => Close(null);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        footer.Children.Add(openButton);
        footer.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        return root;
    }

    private void Initialize()
    {
        var root = new TreeViewItem { Header = "/", Tag = Path.GetPathRoot(Path.GetFullPath("/")) };
        root.Items.Add(new TreeViewItem { Header = "Loading…", Tag = null });
        root.Expanded += (_, _) => LoadFolderChildren(root);
        folders.Items.Add(root);
        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(homePath) && homePath != "/")
        {
            var home = CreateFolderNode(homePath);
            folders.Items.Add(home);
        }

        string initial = requestedPath is not null && Directory.Exists(requestedPath)
            ? requestedPath
            : homePath;
        Navigate(initial);
    }

    private TreeViewItem CreateFolderNode(string path)
    {
        var item = new TreeViewItem { Header = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path, Tag = path };
        item.Items.Add(new TreeViewItem { Header = "Loading…", Tag = null });
        item.Expanded += (_, _) => LoadFolderChildren(item);
        return item;
    }

    private void LoadFolderChildren(TreeViewItem parent)
    {
        if (parent.Tag is not string path || parent.Items.Count != 1 || parent.ItemCount == 0)
            return;
        if (parent.Items[0] is not TreeViewItem { Tag: null })
            return;

        parent.Items.Clear();
        try
        {
            foreach (string child in Directory.EnumerateDirectories(path)
                         .OrderBy(value => Path.GetFileName(value), StringComparer.OrdinalIgnoreCase))
            {
                try { parent.Items.Add(CreateFolderNode(child)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            parent.Items.Add(new TreeViewItem { Header = "(unavailable)", Tag = null, IsEnabled = false });
        }
    }

    private void Navigate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { return; }
        if (!Directory.Exists(fullPath)) return;
        location.Text = fullPath;
        LoadItems();
    }

    private void LoadItems()
    {
        if (string.IsNullOrWhiteSpace(location.Text)) return;
        DisposeThumbnails();
        tiles.Children.Clear();
        selectedPath = null;
        openButton.IsEnabled = false;
        preview.Text = location.Text;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(location.Text)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                tiles.Children.Add(CreateTile(directory, isDirectory: true));
            }

            IEnumerable<FileInfo> files = Directory.EnumerateFiles(location.Text)
                .Select(path => new FileInfo(path))
                .Where(SupportedBySelectedFilter);
            files = sort.SelectedIndex switch
            {
                1 => files.OrderBy(file => file.LastWriteTimeUtc),
                2 => files.OrderBy(file => file.Length),
                _ => files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            };
            if (descending) files = files.Reverse();

            int index = 0;
            foreach (FileInfo file in files)
            {
                if (index++ >= 300) break;
                tiles.Children.Add(CreateTile(file.FullName, isDirectory: false));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            preview.Text = $"Cannot read this folder ({exception.GetType().Name}).";
        }
    }

    private bool SupportedBySelectedFilter(FileInfo file)
    {
        bool image = ImageExtensions.Contains(file.Extension);
        bool video = VideoExtensions.Contains(file.Extension);
        return filter.SelectedIndex switch
        {
            1 => image,
            2 => video,
            _ => image || video
        };
    }

    private Control CreateTile(string path, bool isDirectory)
    {
        string name = isDirectory ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileName(path);
        var image = new Image { Width = 132, Height = 84, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        if (!isDirectory && ImageExtensions.Contains(Path.GetExtension(path)))
        {
            try
            {
                using var source = new Bitmap(path);
                int width = Math.Max(1, source.PixelSize.Width);
                int height = Math.Max(1, source.PixelSize.Height);
                double scale = Math.Min(1d, 320d / Math.Max(width, height));
                Bitmap thumbnail = source.CreateScaledBitmap(
                    new PixelSize(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale))),
                    BitmapInterpolationMode.LowQuality);
                thumbnailBitmaps.Add(thumbnail);
                image.Source = thumbnail;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                image.Source = null;
            }
        }

        var caption = new TextBlock
        {
            Text = isDirectory ? $"📁 {name}" : name,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40,
            Foreground = Brush("#d9e0e8"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var stack = new StackPanel { Spacing = 6, Width = 150, Margin = new Thickness(5) };
        stack.Children.Add(image);
        stack.Children.Add(caption);
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(4), Background = Brush("#2b323b") };
        border.PointerPressed += async (_, e) =>
        {
            if (isDirectory && e.ClickCount > 1)
            {
                Navigate(path);
                return;
            }
            if (!isDirectory)
            {
                Select(path, name);
                if (e.ClickCount > 1) Close(path);
                else if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                {
                    IStorageFile? file = await StorageProvider.TryGetFileFromPathAsync(path);
                    if (file is null) return;
                    var transfer = new DataTransfer();
                    transfer.Add(DataTransferItem.Create(DataFormat.File, file));
                    await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
                }
            }
        };
        return border;
    }

    private void Select(string path, string name)
    {
        selectedPath = path;
        openButton.IsEnabled = true;
        var info = new FileInfo(path);
        preview.Text = $"{name}\n{info.Length:N0} bytes · modified {info.LastWriteTime:g}";
    }

    private void DisposeThumbnails()
    {
        foreach (Bitmap bitmap in thumbnailBitmaps) bitmap.Dispose();
        thumbnailBitmaps.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeThumbnails();
        base.OnClosed(e);
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
