using Avalonia;
using NativeChannels;
using Plugin.AudioStreamer;

namespace AudioStreamer.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.VerifyAndExit = args.Contains("--verify");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .UseNativeChannels(services =>
            {
                services.AddNativeChannels(options => options.AddAudioStreamer());
                services.AddAudioStreamer();
            });
}
