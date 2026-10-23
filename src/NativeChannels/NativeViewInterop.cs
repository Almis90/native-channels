using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NativeChannels.Maui")]
[assembly: InternalsVisibleTo("Plugin.Avalonia.PdfViewer")]
[assembly: InternalsVisibleTo("Plugin.Uno.PdfViewer")]
[assembly: InternalsVisibleTo("NativeChannels.Interop.Avalonia")]
[assembly: InternalsVisibleTo("NativeChannels.Interop.Uno")]

namespace NativeChannels;

public sealed partial class NativeChannelTransport
{
#if ANDROID
    internal Native.Android.NativeViewHost CreateView(
        NativeModuleRegistration factory,
        Android.Content.Context context,
        string instanceId,
        string channelName,
        byte[] arguments
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(factory.AndroidClass, context, _bridge, instanceId, channelName, arguments);
    }
#elif IOS || MACCATALYST || MACOS
    internal Native.Apple.NativeViewHost CreateView(
        NativeModuleRegistration factory,
        string instanceId,
        string channelName,
        byte[] arguments
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LoadFramework(factory.AppleFramework);
        using var data = Foundation.NSData.FromArray(arguments);
        var host = Native.Apple.NativeViewHost.Create(
            factory.AppleClass,
            _bridge,
            instanceId,
            channelName,
            data,
            out var error
        );
        if (host is null)
            throw new ChannelTransportException(error?.LocalizedDescription ?? "Native view creation failed.");
        return host;
    }
#endif
}
