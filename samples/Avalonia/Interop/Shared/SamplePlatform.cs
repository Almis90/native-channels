using Avalonia.Threading;
using NativeChannels;

namespace NativeChannels.Sample;

internal static class SampleThread
{
    public static bool IsMainThread => Dispatcher.UIThread.CheckAccess();
}

internal static class SamplePlatform
{
    public static NativeModuleRegistration CreateModule() =>
        new("dev.nativechannels.sample.SampleModule", "MCSampleModule", "NativeChannelsSample")
        {
#if WINDOWS
            WindowsFactory = () => new WindowsSampleModule(),
#elif LINUX
            LinuxFactory = () =>
                new LinuxNativeModule(
                    "nc_sample",
                    typeof(SamplePlatform).Assembly,
                    ["sample", "sample.ui"],
                    eventChannels: ["sample.events"],
                    dispatcher: AvaloniaChannelDispatchers.UIThread,
                    rawChannels:
                    [
                        "sample.messages.json",
                        "sample.messages.string",
                        "sample.messages.binary",
                        "sample.messages.standard",
                    ]
                ),
#elif BROWSER
            BrowserModule = "./sample.mjs",
#endif
        };
}
