using System.Text.Json;

namespace Kinovea.PortableDesktop.Presentation;

/// <summary>Persisted state for the opt-in Avalonia desktop shell.</summary>
public sealed class PortableApplicationState
{
    public string ActiveWorkspace { get; set; } = "Default";
    public string UiCulture { get; set; } = "en";
    public string Theme { get; set; } = "Dark";
    public string? LastOpenDirectory { get; set; }
    public string? LastOpenPath { get; set; }
    public double WindowWidth { get; set; } = 1120;
    public double WindowHeight { get; set; } = 760;
    public string OpenShortcut { get; set; } = "Ctrl+O";
    public int SyntheticCaptureIntervalMilliseconds { get; set; } = 40;

    public PortableApplicationState Copy() => new()
    {
        ActiveWorkspace = ActiveWorkspace,
        UiCulture = UiCulture,
        Theme = Theme,
        LastOpenDirectory = LastOpenDirectory,
        LastOpenPath = LastOpenPath,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        OpenShortcut = OpenShortcut,
        SyntheticCaptureIntervalMilliseconds = SyntheticCaptureIntervalMilliseconds
    };
}

/// <summary>JSON-backed application state stored under the user's local data directory.</summary>
public sealed class PortableApplicationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string filePath;

    public PortableApplicationStateStore(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kinovea",
            "portable-desktop-state.json");
    }

    public string FilePath => filePath;

    public PortableApplicationState Load()
    {
        if (!File.Exists(filePath))
            return new PortableApplicationState();

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            PortableApplicationState state = JsonSerializer.Deserialize<PortableApplicationState>(stream, JsonOptions)
                ?? new PortableApplicationState();
            try { Validate(state); }
            catch (ArgumentException) { return new PortableApplicationState(); }
            return state;
        }
        catch (JsonException)
        {
            return new PortableApplicationState();
        }
    }

    public void Save(PortableApplicationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state);
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = filePath + ".tmp";
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void Validate(PortableApplicationState state)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveWorkspace))
            throw new ArgumentException("An active workspace name is required.", nameof(state));
        if (state.WindowWidth is < 320 or > 16384 || state.WindowHeight is < 240 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(state), "Window dimensions are outside the supported range.");
        if (state.SyntheticCaptureIntervalMilliseconds is < 10 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(state), "Capture interval must be between 10 and 1000 milliseconds.");
        if (state.Theme is not ("Dark" or "Light"))
            throw new ArgumentException("Theme must be Dark or Light.", nameof(state));
        if (state.UiCulture is not ("en" or "pt-BR"))
            throw new ArgumentException("UI culture must be en or pt-BR.", nameof(state));
        if (state.OpenShortcut is not ("Ctrl+O" or "Ctrl+Shift+O"))
            throw new ArgumentException("Open shortcut must be Ctrl+O or Ctrl+Shift+O.", nameof(state));
    }
}
