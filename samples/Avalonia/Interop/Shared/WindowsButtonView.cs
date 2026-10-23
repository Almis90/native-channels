#if WINDOWS || UNO_SAMPLE
using System.ComponentModel;
using System.Runtime.InteropServices;
#if UNO_SAMPLE
using NativeChannels;
#else
using Avalonia.Platform;
using Avalonia.Threading;
#endif

namespace ChannelExplorer;

// A real Win32 child button. The framework host owns placement; the per-view channel owns its lifetime.
internal sealed class WindowsButtonView :
#if UNO_SAMPLE
    IUnoNativeView
#else
    IAvaloniaNativeView
#endif
{
    private readonly ChannelService _channels;
    private readonly MethodChannel _native;
    private readonly WindowProcedure _procedure;
    private readonly CancellationTokenSource _stopping = new();
    private nint _window,
        _button,
        _previous;
    private string _title;
    private int _count;
    private bool _destroyed;
#if UNO_SAMPLE
    public object NativeElement { get; }
#else
    public IPlatformHandle Handle { get; }
#endif
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

#if UNO_SAMPLE
    public WindowsButtonView(string identity, string title)
    {
        SampleThread.Dispatcher.VerifyAccess();
#else
    public WindowsButtonView(IPlatformHandle parent, string identity, string title)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (parent.HandleDescriptor != "HWND")
            throw new PlatformNotSupportedException("Expected a Win32 parent.");
#endif
        InstanceId = identity;
        _title = title;
        _procedure = ProcessMessage;
        var name = "nativechannels.view." + identity;
#if UNO_SAMPLE
        ChannelHandlerDispatcher dispatch = SampleThread.Dispatcher.DispatchAsync;
        MethodChannel? native = null;
        var transport = new NativeChannelTransport([
            new("", "")
            {
                WindowsFactory = () =>
                    new ViewModule(channels =>
                        native = channels.GetMethodChannel(name, new() { HandlerDispatcher = dispatch })
                    ),
            },
        ]);
        _channels = new(new BinaryMessenger(transport));
        _native = native ?? throw new InvalidOperationException("Native view module was not registered.");
#else
        ChannelHandlerDispatcher dispatch = AvaloniaChannelDispatchers.UIThread;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        _native = transport.WindowsChannels.GetMethodChannel(name, new() { HandlerDispatcher = dispatch });
#endif
        Channel = _channels.GetMethodChannel(name, new() { HandlerDispatcher = dispatch });
        try
        {
#if UNO_SAMPLE
            _window = CreateWindowExW(0, "STATIC", "", 0x80000000, 0, 0, 100, 72, 0, 0, 0, 0);
#else
            _window = CreateWindowExW(0, "STATIC", "", 0x50000000 | 0x02000000, 0, 0, 100, 72, parent.Handle, 0, 0, 0);
#endif
            if (_window == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
#if UNO_SAMPLE
            if (SetWindowLongPtrW(_window, -16, 0x42000000) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
#endif
            _button = CreateWindowExW(0, "BUTTON", title, 0x50010000, 0, 0, 100, 72, _window, 1, 0, 0);
            if (_button == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _previous = SetWindowLongPtrW(_window, -4, Marshal.GetFunctionPointerForDelegate(_procedure));
            if (_previous == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
#if UNO_SAMPLE
            NativeElement = new Uno.UI.NativeElementHosting.Win32NativeWindow(_window);
#else
            Handle = new PlatformHandle(_window, "HWND");
#endif
            Update();
            _native.SetMethodCallHandler(
                async (call, token) =>
                {
                    ObjectDisposedException.ThrowIf(_destroyed, this);
                    switch (call.Method)
                    {
                        case "getState":
                            return new Dictionary<string, object?>
                            {
                                ["instanceId"] = identity,
                                ["title"] = _title,
                                ["count"] = _count,
                                ["uiThread"] = SampleThread.IsMainThread,
                            };
                        case "setTitle":
                            _title =
                                call.GetArguments<string>()
                                ?? throw new ChannelException("invalid_arguments", "Expected title");
                            Update();
                            return null;
                        case "simulateTap":
                            return await TapAsync(token);
                        case "wait":
                            await Task.Delay(Timeout.Infinite, token);
                            return null;
                        default:
                            throw new MethodNotImplementedException(call.Method);
                    }
                }
            );
        }
        catch
        {
            Destroy();
            _ = _channels.DisposeAsync();
            throw;
        }
    }

#if UNO_SAMPLE
    private sealed class ViewModule(Action<IChannelService> register) : IWindowsChannelModule
    {
        public void Register(IChannelService channels) => register(channels);

        public void Dispose() { }
    }
#endif

    private void Update() => SetWindowTextW(_button, $"{_title} · {_count} taps");

    private async Task<string?> TapAsync(CancellationToken token)
    {
        ++_count;
        Update();
        return await _native.InvokeAsync<string>(
            "clicked",
            new Dictionary<string, object?> { ["instanceId"] = InstanceId, ["count"] = _count },
            token
        );
    }

    private async Task ClickAsync()
    {
        try
        {
            await TapAsync(_stopping.Token);
        }
        catch (Exception error)
        {
            if (!_destroyed)
                Console.Error.WriteLine(error);
        }
    }

    private nint ProcessMessage(nint window, uint message, nuint wparam, nint lparam)
    {
        // No exceptions may cross a native window procedure.
        try
        {
            if (message == 5 && _button != 0)
                MoveWindow(_button, 0, 0, (int)((long)lparam & 0xffff), (int)(((long)lparam >> 16) & 0xffff), true);
            if (message == 0x111 && lparam == _button && (wparam >> 16) == 0 && !_destroyed)
            {
                _ = ClickAsync();
                return 0;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
        return CallWindowProcW(_previous, window, message, wparam, lparam);
    }

    public void Destroy()
    {
#if UNO_SAMPLE
        SampleThread.Dispatcher.VerifyAccess();
#else
        Dispatcher.UIThread.VerifyAccess();
#endif
        if (_destroyed)
            return;
        _destroyed = true;
        _stopping.Cancel();
        _native.Dispose();
        if (_previous != 0)
            SetWindowLongPtrW(_window, -4, _previous);
        if (_window != 0)
            DestroyWindow(_window);
        _window = _button = 0;
        _stopping.Dispose();
        GC.KeepAlive(_procedure);
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wparam, nint lparam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extended,
        string className,
        string title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProcW(nint previous, nint window, uint message, nuint wparam, nint lparam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(nint window, string text);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);
}
#endif
