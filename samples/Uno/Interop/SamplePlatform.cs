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
#if IOS
        return new("dev.nativechannels.sample.SampleModule", "MCSampleModule", "NativeChannelsSample");
#else
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Uno native Interop parity suite currently requires Windows.");
        return new("", "") { WindowsFactory = () => new WindowsSampleModule() };
#endif
    }
}
