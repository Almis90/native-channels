using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Xunit;

namespace Plugin.Maui.Currency.Tests;

public sealed class CurrencyRegistrationTests
{
    [Fact]
    public void SharedAndHostSpecificOptionsKeepTheirTypeAndRegisterTheModuleOnce()
    {
        var shared = new ChannelRegistrationOptions();
        Assert.Same(shared, shared.AddCurrency().AddCurrency());
        Assert.Single(shared.Modules);
        var host = new HostOptions();
        HostOptions configured = host.AddCurrency().AddCurrency();
        Assert.Same(host, configured);
        Assert.Equal(Assert.Single(shared.Modules), Assert.Single(host.Modules));
    }

    [Fact]
    public async Task RepeatedServiceRegistrationKeepsOneOwnedCurrencyHandle()
    {
        var pair = InMemoryTransport.CreatePair();
        await using var managed = new ChannelService(new BinaryMessenger(pair.Managed));
        await using var native = new ChannelService(new BinaryMessenger(pair.Native));
        using var endpoint = native.GetMethodChannel("plugin.maui.currency");
        using var registration = endpoint.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>("€"));
        var services = new ServiceCollection();
        services.AddSingleton<IChannelService>(managed);
        services.AddCurrency().AddCurrency();
        await using var provider = services.BuildServiceProvider();
        var currency = provider.GetRequiredService<ICurrencyService>();
        Assert.Same(currency, provider.GetRequiredService<ICurrencyService>());
        Assert.Equal("€", await currency.GetSymbolAsync("EUR"));
        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => currency.GetSymbolAsync("EUR"));
        using var other = new CurrencyService(managed);
        Assert.Equal("€", await other.GetSymbolAsync("EUR"));
    }

    private sealed class HostOptions : ChannelRegistrationOptions;
}
