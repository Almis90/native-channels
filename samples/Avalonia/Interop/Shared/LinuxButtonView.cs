#if LINUX
using Avalonia.Platform;
using Avalonia.Threading;
using NativeChannels;

namespace ChannelExplorer;

internal sealed class LinuxButtonView : IAvaloniaNativeView
{
    private readonly LinuxNativeModule _module;
    private readonly ChannelService _channels;
    private readonly DispatcherTimer _timer;
    private bool _closed;
    public IPlatformHandle Handle { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public LinuxButtonView(IPlatformHandle parent, string identity, string title)
    {
        if (parent.HandleDescriptor != "XID" && parent.HandleDescriptor != "X11")
            throw new PlatformNotSupportedException(
                "The Linux native-button example requires Avalonia's X11 backend (XWayland is supported)."
            );
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        var name = "nativechannels.view." + identity;
        _module = new(
            "nc_sample",
            typeof(SamplePlatform).Assembly,
            [name],
            dispatcher: AvaloniaChannelDispatchers.UIThread
        );
        try
        {
            _module.Register(transport.LinuxChannels);
            Handle = new PlatformHandle(
                _module.CreateView(parent.Handle, identity, new Dictionary<string, object?> { ["title"] = title }),
                "XID"
            );
            Channel = _channels.GetMethodChannel(
                name,
                new() { HandlerDispatcher = AvaloniaChannelDispatchers.UIThread }
            );
            _timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(16),
                DispatcherPriority.Background,
                (_, _) =>
                {
                    if (!_closed)
                        _module.PollView();
                }
            );
            _timer.Start();
        }
        catch
        {
            _module.Dispose();
            _ = _channels.DisposeAsync();
            throw;
        }
    }

    public void Destroy()
    {
        if (_closed)
            return;
        _closed = true;
        _timer.Stop();
        _module.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }
}
#endif
