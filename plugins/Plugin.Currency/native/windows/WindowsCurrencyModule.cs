#if WINDOWS || DESKTOP
using NativeChannels;

namespace Plugin.Currency;

internal sealed class WindowsCurrencyModule : IWindowsChannelModule
{
    private MethodChannel? _channel;

    public void Register(IChannelService channels)
    {
        _channel = channels.GetMethodChannel("plugin.maui.currency");
        _channel.SetMethodCallHandler(HandleAsync);
    }

    private static ValueTask<object?> HandleAsync(MethodCall call, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (call.Method != "getSymbol")
        {
            throw new MethodNotImplementedException(call.Method);
        }
        if (call.Arguments is not string code)
        {
            throw new ChannelException(
                "invalid_currency_code",
                "Expected a three-letter currency code",
                platform: "windows"
            );
        }
        try
        {
            return ValueTask.FromResult<object?>(WindowsCurrencySymbols.GetSymbol(code));
        }
        catch (ArgumentException)
        {
            throw new ChannelException("invalid_currency_code", "Currency code is not recognized", code, "windows");
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _channel, null)?.Dispose();
}
#endif
