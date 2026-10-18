using System.IO;
using Avalonia;
using NativeChannels;
#if MACOS
using AppKit;
#endif

namespace Currency.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.ExitAfterVerification = args.Contains("--verify");
        App.ResultReported = result =>
        {
            var destination = Environment.GetEnvironmentVariable("CURRENCY_AVALONIA_RESULT");
            if (string.IsNullOrWhiteSpace(destination))
                return;
            var temporary = destination + ".tmp";
            File.WriteAllText(temporary, result);
            File.Move(temporary, destination, overwrite: true);
        };
#if MACOS
        NSApplication.Init();
#endif
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().UseNativeChannels(NativeCurrency.ConfigureServices);
    }
}
