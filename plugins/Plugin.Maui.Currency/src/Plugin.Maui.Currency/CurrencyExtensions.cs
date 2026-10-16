using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.Maui.Currency;

public static class CurrencyExtensions
{
    /// <summary>Adds the plugin's packaged native module to a NativeChannels session.</summary>
    public static NativeChannelsOptions AddCurrency(this NativeChannelsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var module = new NativeModuleRegistration(
            "dev.nativechannels.currency.CurrencyModule",
            "MCCurrencyModule",
            "MauiCurrencyNative"
        )
        {
#if WINDOWS
            WindowsFactory = CreateWindowsModule,
#endif
        };
        if (!options.Modules.Contains(module))
        {
            options.Modules.Add(module);
        }
        return options;
    }

    /// <summary>Registers the currency service. Also call options.AddCurrency inside UseNativeChannels.</summary>
    public static IServiceCollection AddCurrency(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICurrencyService, CurrencyService>();
        return services;
    }

#if WINDOWS
    private static IWindowsChannelModule CreateWindowsModule() => new WindowsCurrencyModule();
#endif
}
