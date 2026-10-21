#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Uno.UI.NativeElementHosting;
using NativeChannels;

namespace Plugin.Uno.PdfViewer;

public static partial class PdfBrowser
{
    private static Task<JSObject>? _initialization;

    public static Task InitializeAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document")!;
        return _initialization ??= JSHost.ImportAsync(
            "NativeChannels.Uno.Pdf",
            new Uri(
                new Uri(document.GetPropertyAsString("baseURI")!),
                "./_content/Plugin.Uno.PdfViewer/pdf-uno.mjs"
            ).AbsoluteUri
        );
    }

    [JSImport("createElement", "NativeChannels.Uno.Pdf")]
    internal static partial void CreateView(string identity, string parentId);

    [JSImport("destroyView", "NativeChannels.Uno.Pdf")]
    internal static partial Task DestroyView(string identity);
}

internal sealed class BrowserPdfView : IUnoNativeView
{
    private readonly ChannelService _channels;
    private bool _closed;
    private Task _destroying = Task.CompletedTask;
    private readonly BrowserHtmlElement _element;
    public object NativeElement => _element;
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public BrowserPdfView(string identity)
    {
        InstanceId = identity;
        _element = BrowserHtmlElement.CreateHtmlElement("div");
        PdfBrowser.CreateView(identity, _element.ElementId);
        var transport = new NativeChannelTransport([
            new NativeModuleRegistration("", "", "")
            {
                BrowserModule = "_content/Plugin.Uno.PdfViewer/pdfviewer.mjs?instance=" + identity,
            },
        ]);
        _channels = new(new BinaryMessenger(transport));
        Channel = _channels.GetMethodChannel(
            "nativechannels.view." + identity,
            new()
            {
                HandlerDispatcher = new UnoChannelDispatcher(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ).DispatchAsync,
            }
        );
    }

    public void Destroy()
    {
        if (_closed)
            return;
        _closed = true;
        _destroying = PdfBrowser.DestroyView(InstanceId);
        _element.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        try
        {
            await _destroying;
        }
        finally
        {
            await _channels.DisposeAsync();
        }
    }
}
#endif
