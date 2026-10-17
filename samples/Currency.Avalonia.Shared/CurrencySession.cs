using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Maui.Currency;

namespace Currency.AvaloniaSample;

internal sealed class CurrencySession : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public CurrencySession()
    {
        var services = new ServiceCollection();
        services.AddNativeChannels(options => options.AddCurrency());
        services.AddCurrency();
        _provider = services.BuildServiceProvider();
    }

    public ICurrencyService Currency => _provider.GetRequiredService<ICurrencyService>();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
