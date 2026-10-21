using NativeChannels;
using Plugin.Currency;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "Currency";
    private const string Limitation =
        "Windows, macOS ARM64 desktop, Linux x64 X11, iOS ARM64 simulator and Android x64 emulator verified; other targets pending.";
    private readonly TextBox _code = new() { Header = "ISO currency code", Text = "EUR" };

    private ICurrencyService Currency =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddCurrency());
            services.AddCurrency();
        })
            .Services.GetRequiredService<ICurrencyService>();

    private void Build()
    {
        _status.Text = "Look up a symbol through the native currency plugin.";
        _panel.Children.Add(_code);
        Button(
            "Look up",
            async () => _status.Text = await Currency.GetSymbolAsync(_code.Text.Trim().ToUpperInvariant())
        );
    }

    private async Task VerifyAsync()
    {
        var result = await global::Currency.Verification.PluginVerification.RunAsync(Currency, _stopping.Token);
        Console.WriteLine(result);
#if UNO_BROWSER_SAMPLE
        Console.WriteLine(await global::Currency.Browser.TransportVerification.RunAsync(_window));
#endif
    }

    public Task StopAsync() => Task.CompletedTask;
}
