using NativeChannels;

namespace Plugin.Maui.Currency;

internal sealed class CurrencyService(IChannelService channels) : ICurrencyService, IDisposable
{
    private readonly MethodChannel _channel = channels.GetMethodChannel("plugin.maui.currency");

    public async Task<string> GetSymbolAsync(string currencyCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currencyCode);
        var code = currencyCode.Trim();
        if (code.Length != 3 || code.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("Use a three-letter currency code, such as USD or EUR.", nameof(currencyCode));
        }

        var symbol = await _channel
            .InvokeAsync<string>("getSymbol", code.ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ChannelSerializationException("The native currency module returned an empty symbol.");
        }
        return symbol;
    }

    public void Dispose() => _channel.Dispose();
}
