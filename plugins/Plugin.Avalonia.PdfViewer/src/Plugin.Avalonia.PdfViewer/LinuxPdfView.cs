#if LINUX
using Avalonia.Platform;
using Avalonia.Threading;
using NativeChannels;

namespace Plugin.Avalonia.PdfViewer;

public enum LinuxPdfBackend
{
    Qt,
    Gtk,
}

public static class PdfLinux
{
    public static LinuxPdfBackend Backend { get; set; } = LinuxPdfBackend.Qt;
}

internal sealed class LinuxPdfView : IAvaloniaNativeView
{
    private readonly LinuxNativeModule _module;
    private readonly ChannelService _channels;
    private readonly DispatcherTimer _timer;
    private bool _closed;
    public IPlatformHandle Handle { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public LinuxPdfView(IPlatformHandle parent, string identity)
    {
        if (parent.HandleDescriptor != "XID" && parent.HandleDescriptor != "X11")
            throw new PlatformNotSupportedException(
                "The Linux PDF examples require Avalonia's X11 backend (XWayland is supported)."
            );
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        var name = "nativechannels.view." + identity;
        _module = new(
            PdfLinux.Backend == LinuxPdfBackend.Qt ? "nc_pdf_qt" : "nc_pdf_gtk",
            typeof(PdfViewer).Assembly,
            [name],
            dispatcher: AvaloniaChannelDispatchers.UIThread
        );
        try
        {
            _module.Register(transport.LinuxChannels);
            Handle = new PlatformHandle(_module.CreateView(parent.Handle, identity), "XID");
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
