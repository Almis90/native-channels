using System.Globalization;
using Currency.Verification;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Currency;
using Xunit;

namespace NativeChannels.Linux.Tests;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("USD", "en_US", "$")]
    [InlineData("USD", "en_CA", "US$")]
    [InlineData("EUR", "en_US", "€")]
    [InlineData("GBP", "en_GB", "£")]
    [InlineData("JPY", "ja_JP", "￥")]
    public async Task SystemIcuReturnsLocalizedSymbols(string code, string locale, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale.Replace('_', '-'));
            await using var channels = new ChannelService(
                new BinaryMessenger(new NativeChannelTransport(new ChannelRegistrationOptions().AddCurrency().Modules))
            );
            using var channel = channels.GetMethodChannel("plugin.maui.currency");
            Assert.Equal(expected, await channel.InvokeAsync<string>("getSymbol", code));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("US")]
    [InlineData("usd")]
    [InlineData("U\0D")]
    public async Task InvalidCodesCannotFallThroughToIcuCodeFallback(string code)
    {
        await using var channels = new ChannelService(
            new BinaryMessenger(new NativeChannelTransport(new ChannelRegistrationOptions().AddCurrency().Modules))
        );
        using var channel = channels.GetMethodChannel("plugin.maui.currency");
        var error = await Assert.ThrowsAsync<ChannelException>(() => channel.InvokeAsync("getSymbol", code));
        Assert.Equal("invalid_currency_code", error.Code);
    }

    [Fact]
    public async Task RegisteredPluginPassesSharedScenariosAndRejectsMalformedNativeCalls()
    {
        var services = new ServiceCollection();
        services.AddNativeChannels(options => options.AddCurrency().AddCurrency());
        services.AddCurrency();
        await using var provider = services.BuildServiceProvider();
        var currency = provider.GetRequiredService<ICurrencyService>();
        Assert.StartsWith("PASS linux:", await PluginVerification.RunAsync(currency));
        using var channel = provider.GetRequiredService<IChannelService>().GetMethodChannel("plugin.maui.currency");
        var error = await Assert.ThrowsAsync<ChannelException>(() => channel.InvokeAsync("getSymbol", 42));
        Assert.Equal("linux", error.Platform);
        Assert.Equal("invalid_currency_code", error.Code);
        await Assert.ThrowsAsync<MethodNotImplementedException>(() => channel.InvokeAsync("missing"));
        await Assert.ThrowsAsync<ArgumentException>(() => channel.InvokeAsync("getSymbol\0suffix", "USD"));
        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => currency.GetSymbolAsync("USD"));
    }
}
