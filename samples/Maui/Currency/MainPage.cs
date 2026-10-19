using Currency.Verification;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using NativeSamples;
using Plugin.Currency;

namespace Currency.Sample;

public sealed class MainPage : ContentPage
{
    private readonly ICurrencyService _currency;
    private readonly Entry _code = new()
    {
        Text = "USD",
        Placeholder = "Currency code",
        MaxLength = 12,
        ReturnType = ReturnType.Go,
    };
    private readonly Label _symbol = new()
    {
        Text = "—",
        FontSize = 72,
        TextColor = Color.FromArgb("6554C0"),
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private readonly Label _description = new() { HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _verification = new() { Text = "Checking the packaged native plugin…", FontSize = 12 };
    private readonly Button _lookup = new() { Text = "Get currency symbol" };
    private bool _started;

    public MainPage(ICurrencyService currency)
    {
        _currency = currency;
        Title = "Currency Symbols";
        SafeAreaEdges = SafeAreaEdges.All;
        _lookup.Clicked += async (_, _) => await LookupAsync();
        _code.Completed += async (_, _) => await LookupAsync();
        var shortcuts = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
        foreach (var code in new[] { "USD", "EUR", "GBP", "JPY" })
        {
            var button = new Button { Text = code, FontSize = 13 };
            button.Clicked += async (_, _) =>
            {
                _code.Text = code;
                await LookupAsync();
            };
            shortcuts.Children.Add(button);
        }
        Content = new ScrollView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Content = new VerticalStackLayout
            {
                SafeAreaEdges = SafeAreaEdges.None,
                Padding = 24,
                Spacing = 22,
                MaximumWidthRequest = 520,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "Currency Symbols",
                        FontSize = 30,
                        FontAttributes = FontAttributes.Bold,
                    },
                    new Label { Text = "Enter a currency code to look up its symbol on this device." },
                    _code,
                    _lookup,
                    shortcuts,
                    _symbol,
                    _description,
                    new Label
                    {
                        Text =
                            "Symbols use your device’s language and region. For example, USD may appear as $ or US$.",
                        FontSize = 14,
                    },
                    _verification,
                    new Label
                    {
                        Text =
                            "Try CHF, CAD or AUD for more currencies. Enter ZZZ to see how an unsupported code is reported.",
                        FontSize = 14,
                    },
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }
        _started = true;
        string result;
        try
        {
            result = await PluginVerification.RunAsync(_currency);
            _verification.Text =
                "Native checks passed: lookups, input normalization, error handling, concurrent calls and cancellation.";
        }
        catch (Exception error)
        {
            result = "FAIL " + error;
            _verification.Text = "Native checks failed: " + error.Message;
        }
        Console.WriteLine("CURRENCY_SAMPLE " + result);
        var resultPath = Path.Combine(FileSystem.AppDataDirectory, "currency-result.txt");
#if WINDOWS
        resultPath = Environment.GetEnvironmentVariable("CURRENCY_SAMPLE_RESULT") ?? resultPath;
#endif
        await File.WriteAllTextAsync(resultPath, result);
        if (await LocalVerification.CompleteAsync(this, result))
            return;
        await LookupAsync();
    }

    private async Task LookupAsync()
    {
        _lookup.IsEnabled = false;
        try
        {
            _symbol.Text = await _currency.GetSymbolAsync(_code.Text ?? "");
            _description.Text = _code.Text?.Trim().ToUpperInvariant();
        }
        catch (Exception error)
        {
            _symbol.Text = "—";
            _description.Text = error.Message;
        }
        finally
        {
            _lookup.IsEnabled = true;
        }
    }
}
