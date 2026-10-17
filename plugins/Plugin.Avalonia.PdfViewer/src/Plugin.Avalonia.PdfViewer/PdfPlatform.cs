using Avalonia.Platform;
using NativeChannels;
#if ANDROID
using Avalonia.Android;
#elif IOS
using Avalonia.iOS;
#endif

namespace Plugin.Avalonia.PdfViewer;

internal static partial class PdfPlatform
{
    public static IAvaloniaNativeView Create(IPlatformHandle parent, string identity)
    {
#if ANDROID || IOS || MACOS
        return new MobileAppleView(parent, identity);
#elif WINDOWS
        return new WindowsPdfView(parent, identity);
#elif LINUX
        return new LinuxPdfView(parent, identity);
#elif BROWSER
        return new BrowserPdfView(parent, identity);
#else
        throw new PlatformNotSupportedException("Use a platform-specific PDF package.");
#endif
    }
}

#if ANDROID || IOS || MACOS
internal sealed class MobileAppleView : IAvaloniaNativeView
{
    private readonly ChannelService _channels;
#if ANDROID
    private readonly NativeChannels.Native.Android.NativeViewHost _view;
#else
    private readonly NativeChannels.Native.Apple.NativeViewHost _view;
#endif
    public IPlatformHandle Handle { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }
    private bool _destroyed;

    public MobileAppleView(IPlatformHandle parent, string identity)
    {
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        Channel = _channels.GetMethodChannel(
            "nativechannels.view." + identity,
            new() { HandlerDispatcher = AvaloniaChannelDispatchers.UIThread }
        );
        var factory = new NativeModuleRegistration(
            "dev.nativechannels.pdf.PdfViewFactory",
            "MCPdfViewFactory",
            "MauiPdfViewerNative"
        );
        try
        {
#if ANDROID
            var context = (parent as AndroidViewControlHandle)?.View.Context ?? global::Android.App.Application.Context;
            _view = transport.CreateView(
                factory,
                context,
                identity,
                Channel.Name,
                StandardMessageCodec.Instance.Encode(null).ToArray()
            );
            Handle = new AndroidViewControlHandle(_view.View!);
#else
            _view = transport.CreateView(
                factory,
                identity,
                Channel.Name,
                StandardMessageCodec.Instance.Encode(null).ToArray()
            );
#if IOS
            Handle = new UIViewControlHandle(_view.View!);
#else
            Handle = new PlatformHandle(_view.View!.Handle, "NSView");
#endif
#endif
        }
        catch
        {
            _ = _channels.DisposeAsync();
            throw;
        }
    }

    public void Destroy()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _view.Close();
        _view.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }
}
#endif
