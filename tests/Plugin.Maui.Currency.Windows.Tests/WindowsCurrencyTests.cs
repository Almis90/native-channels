using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Xunit;

namespace Plugin.Maui.Currency.Tests;

public sealed class WindowsCurrencyTests
{
    [Theory]
    [InlineData("USD", "en-US", "US", "$")]
    [InlineData("EUR", "en-US", "US", "€")]
    [InlineData("GBP", "en-US", "US", "£")]
    [InlineData("JPY", "en-US", "US", "¥")]
    [InlineData("EUR", "fr-FR", "FR", "€")]
    // Windows locale data uses either the regular or full-width yen sign for Japanese.
    [InlineData("JPY", "ja-JP", "JP", "¥", "￥")]
    [InlineData("EUR", "ar-SA", "SA", "€")]
    [InlineData("CAD", "en-US", "US", "CAD")]
    public void UsesNativeLocaleSymbolsWithoutFormattingDigits(
        string code,
        string language,
        string region,
        params string[] expectedSymbols
    )
    {
        Assert.Contains(WindowsCurrencySymbols.GetSymbol(code, [language], region), expectedSymbols);
    }

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("US")]
    [InlineData("usd")]
    [InlineData("ＵＳＤ")]
    public void NativeAdapterRejectsMalformedAndUnknownCodes(string code)
    {
        Assert.Throws<ArgumentException>(() => WindowsCurrencySymbols.GetSymbol(code));
    }

    [Fact]
    public async Task PackagedRegistrationIsIdempotentAndCreatesIndependentSessions()
    {
        var options = new NativeChannelsOptions();
        options.AddCurrency().AddCurrency();
        var registration = Assert.Single(options.Modules);
        Assert.NotNull(registration.WindowsFactory);
        using var firstModule = registration.WindowsFactory();
        using var secondModule = registration.WindowsFactory();
        Assert.NotSame(firstModule, secondModule);
        await using var first = new Session(options);
        await using var second = new Session(options);
        Assert.Equal("€", await first.Currency.GetSymbolAsync(" eur "));
        await first.DisposeAsync();
        Assert.Equal("€", await second.Currency.GetSymbolAsync("EUR"));
    }

    [Fact]
    public async Task PackagedModulePreservesProtocolErrorsAndDetails()
    {
        await using var session = new Session();
        using var raw = session.Channels.GetMethodChannel("plugin.maui.currency");
        var error = await Assert.ThrowsAsync<ChannelException>(() => session.Currency.GetSymbolAsync("ZZZ"));
        Assert.Equal("invalid_currency_code", error.Code);
        Assert.Equal("ZZZ", error.Details);
        Assert.Equal("windows", error.Platform);
        var malformed = await Assert.ThrowsAsync<ChannelException>(() => raw.InvokeAsync("getSymbol", 42));
        Assert.Equal("invalid_currency_code", malformed.Code);
        Assert.Equal("windows", malformed.Platform);
        Assert.Null(malformed.Details);
        await Assert.ThrowsAsync<MethodNotImplementedException>(() => raw.InvokeAsync("missing"));
    }

    [Fact]
    public async Task ConcurrentLookupsAndCancellationLeaveTheSessionUsable()
    {
        await using var session = new Session();
        var codes = new[] { "USD", "EUR", "GBP", "JPY" };
        var expected = codes.Select(code => WindowsCurrencySymbols.GetSymbol(code)).ToArray();
        var actual = await Task.WhenAll(codes.Select(code => session.Currency.GetSymbolAsync(code)));
        Assert.Equal(expected, actual);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.Currency.GetSymbolAsync("USD", cancellation.Token)
        );
        Assert.Equal(expected[0], await session.Currency.GetSymbolAsync(" usd "));
    }

    [Fact]
    public async Task ModuleDisposalUnregistersItsHandler()
    {
        var pair = InMemoryTransport.CreatePair();
        await using var managed = new ChannelService(new BinaryMessenger(pair.Managed));
        await using var native = new ChannelService(new BinaryMessenger(pair.Native));
        using var module = new WindowsCurrencyModule();
        module.Register(native);
        using var channel = managed.GetMethodChannel("plugin.maui.currency");
        Assert.Equal("€", await channel.InvokeAsync<string>("getSymbol", "EUR"));
        module.Dispose();
        module.Dispose();
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => channel.InvokeAsync("getSymbol", "EUR"));
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public ChannelService Channels { get; }
        public ICurrencyService Currency { get; }

        public Session(NativeChannelsOptions? options = null)
        {
            options ??= new NativeChannelsOptions().AddCurrency();
            Channels = new(new BinaryMessenger(new NativeChannelTransport(options.Modules)));
            var services = new ServiceCollection();
            services.AddSingleton<IChannelService>(Channels);
            services.AddCurrency().AddCurrency();
            _provider = services.BuildServiceProvider();
            Currency = _provider.GetRequiredService<ICurrencyService>();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await Channels.DisposeAsync();
        }
    }
}
