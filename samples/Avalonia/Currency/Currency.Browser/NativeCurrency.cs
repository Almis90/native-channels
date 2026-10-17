using Currency.Services;
using Currency.Verification;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Maui.Currency;

namespace Currency.Browser;

internal static class NativeCurrency
{
    public static CurrencySession CreateSession()
    {
        var services = new ServiceCollection();
        services.AddNativeChannels(options => options.AddCurrency());
        services.AddCurrency();
        var provider = services.BuildServiceProvider();
        var currency = provider.GetRequiredService<ICurrencyService>();
        return new CurrencySession(
            currency.GetSymbolAsync,
            token => PluginVerification.RunAsync(currency, token),
            provider.DisposeAsync
        );
    }
}
