using Avalonia;
using NativeChannels;

namespace ReferenceSamples;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder
            .Configure<SampleApplication>()
            .UsePlatformDetect()
            .UseNativeChannels(SampleFeature.Configure)
            .StartWithClassicDesktopLifetime(args);
}
