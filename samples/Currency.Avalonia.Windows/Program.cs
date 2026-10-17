using Avalonia;

namespace Currency.AvaloniaWindows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        return App.VerificationFailed ? 1 : 0;
    }
}
