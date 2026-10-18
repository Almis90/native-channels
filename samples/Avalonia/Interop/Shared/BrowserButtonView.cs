#if BROWSER
using System.Runtime.InteropServices.JavaScript;
using Avalonia.Browser;
using Avalonia.Platform;
using NativeChannels;

namespace ChannelExplorer;

public static partial class SampleBrowser
{
    private static Task<JSObject>? _initialization;

    public static Task InitializeAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document")!;
        return _initialization ??= JSHost.ImportAsync(
            "NativeChannels.SampleButton",
            new Uri(new Uri(document.GetPropertyAsString("baseURI")!), "./button-dom.mjs").AbsoluteUri
        );
    }

    [JSImport("createView", "NativeChannels.SampleButton")]
    internal static partial JSObject CreateView(string identity, string title);

    [JSImport("destroyView", "NativeChannels.SampleButton")]
    internal static partial void DestroyView(string identity);
}

internal sealed class BrowserButtonView : IAvaloniaNativeView
{
    private readonly ChannelService _channels;
    private bool _closed;
    public IPlatformHandle Handle { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public BrowserButtonView(IPlatformHandle parent, string identity, string title)
    {
        InstanceId = identity;
        Handle = new JSObjectControlHandle(SampleBrowser.CreateView(identity, title));
        var transport = new NativeChannelTransport([
            new NativeModuleRegistration("", "", "") { BrowserModule = "./button-module.mjs?instance=" + identity },
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
        SampleBrowser.DestroyView(InstanceId);
        ((JSObjectControlHandle)Handle).Destroy();
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        await _channels.DisposeAsync();
    }
}
#endif
