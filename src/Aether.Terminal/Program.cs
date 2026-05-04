using Avalonia;

namespace Aether.Terminal;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
