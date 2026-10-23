using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace NativeChannels;

// Reuses the same Swift factory and per-view channel as MAUI and Avalonia.
internal sealed class UnoMacOSNativeView : IUnoNativeView
{
    private readonly ChannelService _channels;
    private readonly MacOSNativeView _wrapper;
    private nint _host;
    public object NativeElement => _wrapper.NativeElement;
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public UnoMacOSNativeView(NativeModuleRegistration factory, string identity, byte[] arguments)
    {
        var dispatcher = new UnoChannelDispatcher(DispatcherQueue.GetForCurrentThread());
        dispatcher.VerifyAccess();
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        Channel = _channels.GetMethodChannel(
            "nativechannels.view." + identity,
            new() { HandlerDispatcher = dispatcher.DispatchAsync }
        );
        try
        {
            _host = transport.CreateAppleDesktopView(factory, identity, Channel.Name, arguments);
            _wrapper = new MacOSNativeView(GetView(_host));
        }
        catch
        {
            if (_host != 0)
                DestroyView(_host);
            _host = 0;
            _ = _channels.DisposeAsync();
            throw;
        }
    }

    public void Destroy()
    {
        if (_host == 0)
            return;
        _wrapper.Dispose();
        DestroyView(_host);
        _host = 0;
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }

    [DllImport("NativeChannelsNative", EntryPoint = "nc_apple_view_handle")]
    private static extern nint GetView(nint host);

    [DllImport("NativeChannelsNative", EntryPoint = "nc_apple_view_destroy")]
    private static extern void DestroyView(nint host);
}
