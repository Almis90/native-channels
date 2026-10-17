using Avalonia;
using Avalonia.iOS;
using Foundation;
using NativeChannels;
using UIKit;

namespace ReferenceSamples;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<SampleApplication>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).UseNativeChannels(SampleFeature.Configure);

    [Export("applicationDidEnterBackground:")]
    public void EnteredBackground(UIApplication app) =>
        _ = (Avalonia.Application.Current as SampleApplication)?.SuspendAsync();

    [Export("applicationWillTerminate:")]
    public void WillTerminate(UIApplication app) =>
        _ = (Avalonia.Application.Current as SampleApplication)?.CloseAsync();
}

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
