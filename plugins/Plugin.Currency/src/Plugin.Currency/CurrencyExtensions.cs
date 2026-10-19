using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.Currency;

public static class CurrencyExtensions
{
    /// <summary>Adds the plugin's packaged native module to a NativeChannels session.</summary>
    public static TOptions AddCurrency<TOptions>(this TOptions options)
        where TOptions : ChannelRegistrationOptions
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
#if LINUX
            LinuxFactory = CreateLinuxModule,
#endif
#if BROWSER
            BrowserModule = "./_content/Plugin.Currency/currency.mjs",
#endif
        };
        if (!options.Modules.Contains(module))
        {
            options.Modules.Add(module);
        }
        return options;
    }

    /// <summary>Registers the currency service. Also call options.AddCurrency when registering the channel session.</summary>
    public static IServiceCollection AddCurrency(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICurrencyService, CurrencyService>();
        return services;
    }

#if WINDOWS
    private static IWindowsChannelModule CreateWindowsModule() => new WindowsCurrencyModule();
#endif
#if LINUX
    private static ILinuxChannelModule CreateLinuxModule() =>
        new LinuxNativeModule("libnc_currency.so", typeof(CurrencyExtensions).Assembly, ["plugin.maui.currency"]);
#endif
}
