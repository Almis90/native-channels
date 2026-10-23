using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using NativeChannels;
using Plugin.Currency;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "Currency";
    private const string Limitation = "Currency lookups use the platform's language and region.";
    private readonly TextBox _code = new()
    {
        PlaceholderText = "Currency code",
        Text = "USD",
        MaxLength = 12,
    };
    private readonly TextBlock _symbol = new()
    {
        Text = "—",
        FontSize = 72,
        TextAlignment = TextAlignment.Center,
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 101, 84, 192)),
    };
    private readonly TextBlock _description = new()
    {
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _lookup = new()
    {
        Content = "Get currency symbol",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        MinHeight = 44,
    };
    private Task<string>? _currencyVerification;
    private Task _currencyStartup = Task.CompletedTask;
    private bool _currencyStarted;

    private ICurrencyService Currency =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddCurrency());
            services.AddCurrency();
        })
            .Services.GetRequiredService<ICurrencyService>();

    private void Build()
    {
        _status.Text = "Checking the packaged native plugin…";
        _status.FontSize = 12;
        _panel.Children.Clear();
        _panel.Margin = new Thickness(24);
        _panel.MaxWidth = 472;
        _panel.Spacing = 22;
        _lookup.Click += async (_, _) => await LookupAsync();
        _code.KeyDown += async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                await LookupAsync();
            }
        };
        var shortcuts = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var code in new[] { "USD", "EUR", "GBP", "JPY" })
        {
            var button = new Button
            {
                Content = code,
                FontSize = 13,
                MinWidth = 56,
                MinHeight = 44,
            };
            button.Click += async (_, _) =>
            {
                _code.Text = code;
                await LookupAsync();
            };
            shortcuts.Children.Add(button);
        }
        _panel.Children.Add(
            new TextBlock
            {
                Text = "Currency Symbols",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
            }
        );
        _panel.Children.Add(
            new TextBlock
            {
                Text = "Enter a currency code to look up its symbol on this device.",
                TextWrapping = TextWrapping.Wrap,
            }
        );
        _panel.Children.Add(_code);
        _panel.Children.Add(_lookup);
        _panel.Children.Add(shortcuts);
        _panel.Children.Add(_symbol);
        _panel.Children.Add(_description);
        _panel.Children.Add(
            new TextBlock
            {
                Text = "Symbols use your device’s language and region. For example, USD may appear as $ or US$.",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            }
        );
        _panel.Children.Add(_status);
        _panel.Children.Add(
            new TextBlock
            {
                Text = "Try CHF, CAD or AUD for more currencies. Enter ZZZ to see how an unsupported code is reported.",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            }
        );
        var header = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 44, 62, 80)),
            Padding = new Thickness(24, 14, 24, 14),
            Child = new TextBlock
            {
                Text = "Currency Symbols",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextAlignment = TextAlignment.Center,
            },
        };
        var content = new ScrollViewer { Content = _panel, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        layout.Children.Add(header);
        layout.Children.Add(content);
        Content = layout;
        Loaded += async (_, _) =>
        {
            if (_currencyStarted)
                return;
            _currencyStarted = true;
            _currencyStartup = InitializeCurrencyAsync();
            await _currencyStartup;
        };
    }

    private Task<string> VerifyCurrencyAsync() =>
        _currencyVerification ??= global::Currency.Verification.PluginVerification.RunAsync(Currency, _stopping.Token);

    private async Task InitializeCurrencyAsync()
    {
        try
        {
            Console.WriteLine(await VerifyCurrencyAsync());
            _status.Text =
                "Native checks passed: lookups, input normalization, error handling, concurrent calls and cancellation.";
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            _status.Text = "Native checks failed: " + error.Message;
        }
        await LookupAsync();
    }

    private async Task LookupAsync()
    {
        if (_stopping.IsCancellationRequested)
            return;
        _lookup.IsEnabled = false;
        try
        {
            _symbol.Text = await Currency.GetSymbolAsync(_code.Text ?? "", _stopping.Token);
            _description.Text = _code.Text?.Trim().ToUpperInvariant();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            _symbol.Text = "—";
            _description.Text = error.Message;
        }
        finally
        {
            _lookup.IsEnabled = !_stopping.IsCancellationRequested;
        }
    }

    private async Task VerifyAsync()
    {
        Console.WriteLine(await VerifyCurrencyAsync());
#if UNO_BROWSER_SAMPLE
        Console.WriteLine(await global::Currency.Browser.TransportVerification.RunAsync(_window));
#endif
    }

    public async Task StopAsync()
    {
        if (_stopping.IsCancellationRequested)
            await _currencyStartup;
    }
}
