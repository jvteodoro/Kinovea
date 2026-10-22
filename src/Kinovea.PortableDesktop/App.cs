using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Kinovea.PortableDesktop.Presentation;

namespace Kinovea.PortableDesktop;

internal sealed class App : Application
{
    public PortableApplicationStateStore StateStore { get; } = new();
    public PortableApplicationState State { get; private set; } = new();

    public override void Initialize()
    {
        State = StateStore.Load();
        RequestedThemeVariant = State.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(DesktopArguments.Value, State, StateStore);
        base.OnFrameworkInitializationCompleted();
    }
}
