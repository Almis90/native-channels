#if IOS
using Microsoft.UI.Dispatching;
using NativeChannels;

namespace Plugin.Uno.PdfViewer;

internal sealed class ApplePdfView : IUnoNativeView
{
    private readonly ChannelService _channels;
    private readonly NativeChannels.Native.Apple.NativeViewHost _view;
    private bool _destroyed;
    public object NativeElement { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public ApplePdfView(string identity)
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
            _view = transport.CreateView(
                new("dev.nativechannels.pdf.PdfViewFactory", "MCPdfViewFactory", "MauiPdfViewerNative"),
                identity,
                Channel.Name,
                StandardMessageCodec.Instance.Encode(null).ToArray()
            );
            NativeElement = _view.View!;
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
