using NativeChannels;

namespace ChannelExplorer;

internal static class SampleThread
{
    public static UnoChannelDispatcher Dispatcher { get; set; } = null!;
    public static bool IsMainThread => Dispatcher.HasThreadAccess;
}

internal static class SamplePlatform
{
    public static NativeModuleRegistration CreateModule()
    {
#if UNO_BROWSER_SAMPLE
        return new("", "", "") { BrowserModule = "./sample.mjs" };
#elif UNO_LINUX_SAMPLE
        return new("", "")
        {
            LinuxFactory = () =>
                new LinuxNativeModule(
                    "nc_sample",
                    typeof(SamplePlatform).Assembly,
                    ["sample", "sample.ui"],
                    eventChannels: ["sample.events"],
                    dispatcher: SampleThread.Dispatcher.DispatchAsync,
                    rawChannels:
                    [
                        "sample.messages.json",
                        "sample.messages.string",
                        "sample.messages.binary",
                        "sample.messages.standard",
                    ]
                ),
        };
#elif IOS || ANDROID
        return new("dev.nativechannels.sample.SampleModule", "MCSampleModule", "NativeChannelsSample");
#else
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Uno native Interop parity suite currently requires Windows.");
        return new("", "") { WindowsFactory = () => new WindowsSampleModule() };
#endif
    }
}
