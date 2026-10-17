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
    public void SystemIcuReturnsLocalizedSymbols(string code, string locale, string expected) =>
        Assert.Equal(expected, LinuxCurrencySymbols.GetSymbol(code, locale));

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("US")]
    [InlineData("usd")]
    [InlineData("U\0D")]
    public void InvalidCodesCannotFallThroughToIcuCodeFallback(string code) =>
        Assert.Throws<ArgumentException>(() => LinuxCurrencySymbols.GetSymbol(code));

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
        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => currency.GetSymbolAsync("USD"));
    }
}
