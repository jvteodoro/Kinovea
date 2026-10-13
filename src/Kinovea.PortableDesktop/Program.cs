using Avalonia;

namespace Kinovea.PortableDesktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        DesktopArguments.Value = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

internal static class DesktopArguments
{
    public static string[] Value { get; set; } = [];
}
