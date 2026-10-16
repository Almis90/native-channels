using NativeChannels;
using Xunit;

namespace Plugin.Maui.Currency.Tests;

public sealed class CurrencyServiceTests : IAsyncLifetime
{
    private readonly ChannelService _managed;
    private readonly ChannelService _native;
    private readonly CurrencyService _currency;
    private readonly MethodChannel _endpoint;

    public CurrencyServiceTests()
    {
        var pair = InMemoryTransport.CreatePair();
        _managed = new(new BinaryMessenger(pair.Managed));
        _native = new(new BinaryMessenger(pair.Native));
        _currency = new(_managed);
        _endpoint = _native.GetMethodChannel("plugin.maui.currency");
    }

    [Fact]
    public async Task NormalizesCodesAndPreservesNativeSymbols()
    {
        using var registration = _endpoint.SetMethodCallHandler(
            (call, _) =>
            {
                Assert.Equal("getSymbol", call.Method);
                Assert.Equal("USD", call.Arguments);
                return ValueTask.FromResult<object?>("US$");
            }
        );
        Assert.Equal("US$", await _currency.GetSymbolAsync(" usd "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("123")]
    [InlineData("ＵＳＤ")]
    [InlineData("ısd")]
    public async Task RejectsMalformedInputBeforeSending(string code)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _currency.GetSymbolAsync(code));
    }

    [Fact]
    public async Task RejectsNullInput()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _currency.GetSymbolAsync(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(42)]
    public async Task RejectsInvalidNativeResults(object? value)
    {
        using var registration = _endpoint.SetMethodCallHandler((_, _) => ValueTask.FromResult(value));
        await Assert.ThrowsAsync<ChannelSerializationException>(() => _currency.GetSymbolAsync("USD"));
    }

    [Fact]
    public async Task PreservesNativeErrorDetails()
    {
        using var registration = _endpoint.SetMethodCallHandler(
            (_, _) =>
                throw new ChannelException("invalid_currency_code", "Currency code is not recognized", "ZZZ", "android")
        );
        var error = await Assert.ThrowsAsync<ChannelException>(() => _currency.GetSymbolAsync("ZZZ"));
        Assert.Equal("invalid_currency_code", error.Code);
        Assert.Equal("ZZZ", error.Details);
        Assert.Equal("android", error.Platform);
    }

    [Fact]
    public async Task ForwardsCancellationToAnActiveNativeCall()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _endpoint.SetMethodCallHandler(
            async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "$";
            }
        );
        using var cancellation = new CancellationTokenSource();
        var pending = _currency.GetSymbolAsync("USD", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task DisposingServiceLeavesOtherPluginHandlesUsable()
    {
        using var registration = _endpoint.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>("€"));
        using var other = new CurrencyService(_managed);
        _currency.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _currency.GetSymbolAsync("EUR"));
        Assert.Equal("€", await other.GetSymbolAsync("EUR"));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _currency.Dispose();
        _endpoint.Dispose();
        await _managed.DisposeAsync();
        await _native.DisposeAsync();
    }
}
