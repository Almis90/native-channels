using NativeChannels;
using Plugin.Currency;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "Currency";
    private const string Limitation = "Windows desktop verified; other platforms pending.";
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
    }

    public Task StopAsync() => Task.CompletedTask;
}
