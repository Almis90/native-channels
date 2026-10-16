using NativeChannels;
using Plugin.Maui.Currency;

namespace Currency.Sample;

internal static class PluginVerification
{
    public static async Task<string> RunAsync(ICurrencyService currency)
    {
#if ANDROID
        const string platform = "android";
#elif WINDOWS
        const string platform = "windows";
#else
        const string platform = "apple";
#endif
        var codes = new[] { "USD", "EUR", "GBP", "JPY" };
        var symbols = await Task.WhenAll(codes.Select(code => currency.GetSymbolAsync(code)));
        Check(symbols.All(symbol => !string.IsNullOrWhiteSpace(symbol)), "Empty native symbol");
        Check(await currency.GetSymbolAsync(" usd ") == symbols[0], "Code normalization failed");
        Check(await currency.GetSymbolAsync("EUR") == "€", "Unexpected EUR symbol");
        var error = await ExpectAsync<ChannelException>(() => currency.GetSymbolAsync("ZZZ"));
        Check(
            error.Code == "invalid_currency_code" && error.Platform == platform && Equals(error.Details, "ZZZ"),
            "Native error was not preserved"
        );
        await ExpectAsync<ArgumentException>(() => currency.GetSymbolAsync("US"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await ExpectAsync<OperationCanceledException>(() => currency.GetSymbolAsync("USD", cancellation.Token));
        Check(await currency.GetSymbolAsync("USD") == symbols[0], "Lookup failed after cancellation");
        return $"PASS {platform}: "
            + string.Join(", ", codes.Zip(symbols, (code, symbol) => $"{code}={symbol}"))
            + "; normalization, errors, concurrency, cancellation";
    }

    private static async Task<T> ExpectAsync<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T error)
        {
            return error;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
