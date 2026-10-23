#if UNO_BROWSER_SAMPLE
using System.Runtime.InteropServices.JavaScript;
using NativeChannels;
using Uno.UI.NativeElementHosting;

namespace ChannelExplorer;

internal sealed partial class UnoBrowserButtonView : IUnoNativeView
{
    private readonly ChannelService _channels;
    private readonly BrowserHtmlElement _element;
    private bool _closed;
    public object NativeElement => _element;
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public static async Task InitializeAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document")!;
        await JSHost.ImportAsync(
            "NativeChannels.Uno.Button",
            new Uri(new Uri(document.GetPropertyAsString("baseURI")!), "./browser-button.mjs").AbsoluteUri
        );
    }

    public UnoBrowserButtonView(string identity, string title)
    {
        InstanceId = identity;
        _element = BrowserHtmlElement.CreateHtmlElement("div");
        CreateElement(identity, title, _element.ElementId);
        var dispatcher = SampleThread.Dispatcher;
        var transport = new NativeChannelTransport([
            new NativeModuleRegistration("", "", "") { BrowserModule = "./button-module.mjs?instance=" + identity },
        ]);
        _channels = new(new BinaryMessenger(transport));
        Channel = _channels.GetMethodChannel(
            "nativechannels.view." + identity,
            new() { HandlerDispatcher = dispatcher.DispatchAsync }
        );
    }

    public void Destroy()
    {
        if (_closed)
            return;
        _closed = true;
        DestroyView(InstanceId);
        _element.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }

    [JSImport("createElement", "NativeChannels.Uno.Button")]
    private static partial void CreateElement(string identity, string title, string parentId);

    [JSImport("destroyView", "NativeChannels.Uno.Button")]
    private static partial void DestroyView(string identity);
}
#endif
