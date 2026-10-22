using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Kinovea.PortableDesktop.Presentation;

namespace Kinovea.PortableDesktop;

internal enum CaptureLayout
{
    Single,
    Dual
}

internal sealed class PortableDesktopMenu : Menu
{
    public event EventHandler? OpenImageRequested;
    public event EventHandler? CloseMediaRequested;
    public event EventHandler? PreferencesRequested;
    public event EventHandler<CaptureLayout>? CaptureRequested;
    public event EventHandler? SaveWorkspaceRequested;

    public PortableDesktopMenu(PortableApplicationState state)
    {
        PortableDesktopStrings strings = new(state.UiCulture);
        var file = new MenuItem { Header = strings.File };
        var open = new MenuItem { Header = strings.OpenImage, InputGesture = Gesture(state.OpenShortcut) };
        open.Click += (_, _) => OpenImageRequested?.Invoke(this, EventArgs.Empty);
        var close = new MenuItem { Header = strings.Close, InputGesture = new KeyGesture(Key.W, KeyModifiers.Control) };
        close.Click += (_, _) => CloseMediaRequested?.Invoke(this, EventArgs.Empty);
        var preferences = new MenuItem { Header = strings.Preferences, InputGesture = new KeyGesture(Key.OemComma, KeyModifiers.Control) };
        preferences.Click += (_, _) => PreferencesRequested?.Invoke(this, EventArgs.Empty);
        file.Items.Add(open);
        file.Items.Add(close);
        file.Items.Add(new Separator());
        file.Items.Add(preferences);

        var capture = new MenuItem { Header = strings.Capture };
        var single = new MenuItem { Header = strings.SingleCapture };
        single.Click += (_, _) => CaptureRequested?.Invoke(this, CaptureLayout.Single);
        var dual = new MenuItem { Header = strings.DualCapture };
        dual.Click += (_, _) => CaptureRequested?.Invoke(this, CaptureLayout.Dual);
        capture.Items.Add(single);
        capture.Items.Add(dual);

        var workspace = new MenuItem { Header = strings.Workspace };
        var saveWorkspace = new MenuItem { Header = strings.SaveWorkspace };
        saveWorkspace.Click += (_, _) => SaveWorkspaceRequested?.Invoke(this, EventArgs.Empty);
        workspace.Items.Add(new MenuItem { Header = state.ActiveWorkspace, IsEnabled = false });
        workspace.Items.Add(new Separator());
        workspace.Items.Add(saveWorkspace);

        Items.Add(file);
        Items.Add(capture);
        Items.Add(workspace);
    }

    private static KeyGesture Gesture(string shortcut) => shortcut == "Ctrl+Shift+O"
        ? new KeyGesture(Key.O, KeyModifiers.Control | KeyModifiers.Shift)
        : new KeyGesture(Key.O, KeyModifiers.Control);
}
