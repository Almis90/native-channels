using Currency.Services;
using Currency.Verification;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Currency;

namespace Currency.Android;

internal static class NativeCurrency
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddNativeChannels(options => options.AddCurrency());
        services.AddCurrency();
        services.AddSingleton(provider =>
        {
            var currency = provider.GetRequiredService<ICurrencyService>();
            return new CurrencySession(currency.GetSymbolAsync, token => PluginVerification.RunAsync(currency, token));
        });
    }
}
