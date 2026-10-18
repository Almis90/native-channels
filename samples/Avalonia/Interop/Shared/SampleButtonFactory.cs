using Avalonia.Platform;
using NativeChannels;
#if ANDROID
using Avalonia.Android;
#elif IOS
using Avalonia.iOS;
#endif

namespace NativeChannels.Sample;

internal static partial class SampleButtonFactory
{
    public static IAvaloniaNativeView Create(IPlatformHandle parent, string identity, string title)
    {
#if ANDROID || IOS || MACOS
        return new MobileButtonView(parent, identity, title);
#elif WINDOWS
        return new WindowsButtonView(parent, identity, title);
#elif LINUX
        return new LinuxButtonView(parent, identity, title);
#elif BROWSER
        return new BrowserButtonView(parent, identity, title);
#else
        throw new PlatformNotSupportedException("Use a supported platform head.");
#endif
    }
}

#if ANDROID || IOS || MACOS
internal sealed class MobileButtonView : IAvaloniaNativeView
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

    public MobileButtonView(IPlatformHandle parent, string identity, string title)
    {
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        Channel = _channels.GetMethodChannel(
            "nativechannels.view." + identity,
            new() { HandlerDispatcher = AvaloniaChannelDispatchers.UIThread }
        );
        var factory = new NativeModuleRegistration(
            "dev.nativechannels.sample.ButtonViewFactory",
            "MCButtonViewFactory",
            "NativeChannelsSample"
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
                new JsonMessageCodec().Encode(new Dictionary<string, object?> { ["title"] = title }).ToArray()
            );
            Handle = new AndroidViewControlHandle(_view.View!);
#else
            _view = transport.CreateView(
                factory,
                identity,
                Channel.Name,
                new JsonMessageCodec().Encode(new Dictionary<string, object?> { ["title"] = title }).ToArray()
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
