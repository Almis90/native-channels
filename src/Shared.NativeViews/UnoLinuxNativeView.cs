using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace NativeChannels;

// Shared source for the optional Uno PDF adapter and the native Interop sample.
internal sealed class UnoLinuxNativeView : IUnoNativeView
{
    private readonly LinuxNativeModule _module;
    private readonly ChannelService _channels;
    private readonly DispatcherTimer _timer;
    private bool _closed;
    public object NativeElement { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public UnoLinuxNativeView(string library, Assembly owner, string identity, object? arguments = null)
    {
        var dispatcher = new UnoChannelDispatcher(DispatcherQueue.GetForCurrentThread());
        dispatcher.VerifyAccess();
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        var name = "nativechannels.view." + identity;
        _module = new(library, owner, [name], dispatcher: dispatcher.DispatchAsync);
        try
        {
            _module.Register(transport.LinuxChannels);
            var display = XOpenDisplay(0);
            if (display == 0)
                throw new PlatformNotSupportedException("The Uno Linux native view requires an X11 display.");
            nint window;
            try
            {
                // Uno reparents the window into its host when the presenter attaches.
                window = _module.CreateView(XDefaultRootWindow(display), identity, arguments);
            }
            finally
            {
                XCloseDisplay(display);
            }
            NativeElement = new global::Uno.UI.NativeElementHosting.X11NativeWindow(window);
            Channel = _channels.GetMethodChannel(name, new() { HandlerDispatcher = dispatcher.DispatchAsync });
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) =>
            {
                if (!_closed)
                    _module.PollView();
            };
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

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint name);

    [DllImport("libX11.so.6")]
    private static extern nint XDefaultRootWindow(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);
}
