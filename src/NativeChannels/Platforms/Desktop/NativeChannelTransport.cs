#if DESKTOP
namespace NativeChannels;

public sealed partial class NativeChannelTransport
{
    private AppleDesktopTransport? _appleDesktop;

    private partial void Initialize(IEnumerable<NativeModuleRegistration> modules)
    {
        if (OperatingSystem.IsWindows())
            InitializeWindows(modules);
        else if (OperatingSystem.IsLinux())
            InitializeLinux(modules);
        else if (OperatingSystem.IsMacOS())
        {
            _appleDesktop = new AppleDesktopTransport(modules);
            _appleDesktop.SetMessageHandler(ReceiveAsync);
        }
        else
            throw new PlatformNotSupportedException("The desktop transport requires Windows, Linux, or macOS.");
    }

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed)
    {
        if (OperatingSystem.IsWindows())
            return SendWindowsNative(request, completed);
        if (OperatingSystem.IsLinux())
            return SendLinuxNative(request, completed);
        return _appleDesktop!.Send(request, completed);
    }

    private partial void CancelNative(Guid id)
    {
        if (OperatingSystem.IsWindows())
            CancelWindowsNative(id);
        else if (OperatingSystem.IsLinux())
            CancelLinuxNative(id);
        else
            _appleDesktop!.Cancel(id);
    }

    private partial Task ShutdownNativeAsync()
    {
        if (OperatingSystem.IsWindows())
            return ShutdownWindowsNativeAsync();
        if (OperatingSystem.IsLinux())
            return ShutdownLinuxNativeAsync();
        return _appleDesktop!.DisposeAsync().AsTask();
    }
}
#endif
