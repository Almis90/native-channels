#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Avalonia.Browser;
using Avalonia.Platform;
using NativeChannels;

namespace Plugin.Avalonia.PdfViewer;

public static partial class PdfBrowser
{
    private static Task<JSObject>? _initialization;

    public static Task InitializeAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document")!;
        return _initialization ??= JSHost.ImportAsync(
            "NativeChannels.Pdf",
            new Uri(
                new Uri(document.GetPropertyAsString("baseURI")!),
                "./_content/Plugin.Avalonia.PdfViewer/pdf-dom.mjs"
            ).AbsoluteUri
        );
    }

    [JSImport("createView", "NativeChannels.Pdf")]
    internal static partial JSObject CreateView(string identity);

    [JSImport("destroyView", "NativeChannels.Pdf")]
    internal static partial void DestroyView(string identity);
}

internal sealed class BrowserPdfView : IAvaloniaNativeView
{
    private readonly ChannelService _channels;
    private bool _closed;
    public IPlatformHandle Handle { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public BrowserPdfView(IPlatformHandle parent, string identity)
    {
        InstanceId = identity;
        Handle = new JSObjectControlHandle(PdfBrowser.CreateView(identity));
        var transport = new NativeChannelTransport([
            new NativeModuleRegistration("", "", "")
            {
                BrowserModule = "_content/Plugin.Avalonia.PdfViewer/pdfviewer.mjs?instance=" + identity,
            },
        ]);
        _channels = new(new BinaryMessenger(transport));
        Channel = _channels.GetMethodChannel(
            "nativechannels.view." + identity,
            new() { HandlerDispatcher = AvaloniaChannelDispatchers.UIThread }
        );
    }

    public void Destroy()
    {
        if (_closed)
            return;
        _closed = true;
        PdfBrowser.DestroyView(InstanceId);
        ((JSObjectControlHandle)Handle).Destroy();
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }
}
#endif
