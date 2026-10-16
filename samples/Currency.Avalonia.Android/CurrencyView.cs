using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Currency.Verification;

namespace Currency.AvaloniaAndroid;

internal sealed class CurrencyView : UserControl
{
    private readonly CurrencySession _session = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _code = new()
    {
        Text = "USD",
        Watermark = "Currency code",
        MaxLength = 12,
    };
    private readonly TextBlock _symbol = new()
    {
        Text = "—",
        FontSize = 72,
        TextAlignment = TextAlignment.Center,
    };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _verification = new()
    {
        Text = "Checking the packaged native plugin…",
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _lookup = new() { Content = "Get currency symbol" };
    private bool _started;
    private bool _closed;

    public CurrencyView()
    {
        _lookup.Click += async (_, _) => await LookupAsync();
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 48, 24, 24),
                Spacing = 20,
                MaxWidth = 520,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Currency Symbols",
                        FontSize = 30,
                        FontWeight = FontWeight.Bold,
                    },
                    new TextBlock
                    {
                        Text = "Enter a currency code to look up its symbol on this device.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _code,
                    _lookup,
                    _symbol,
                    _description,
                    new TextBlock
                    {
                        Text = "Symbols use your device’s language and region. Try USD, EUR, GBP or JPY.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _verification,
                },
            },
        };
        Loaded += async (_, _) => await VerifyAsync();
    }

    private async Task VerifyAsync()
    {
        if (_started || _closed)
        {
            return;
        }
        _started = true;
        string result;
        try
        {
            result = await PluginVerification.RunAsync(_session.Currency, _lifetime.Token);
            _verification.Text =
                "Native checks passed: lookups, normalization, errors, concurrent calls and cancellation.";
        }
        catch (OperationCanceledException) when (_closed)
        {
            return;
        }
        catch (Exception error)
        {
            result = "FAIL " + error;
            _verification.Text = "Native checks failed: " + error.Message;
        }
        Console.WriteLine("CURRENCY_AVALONIA " + result);
        await LookupAsync();
    }

    private async Task LookupAsync()
    {
        if (_closed)
        {
            return;
        }
        _lookup.IsEnabled = false;
        try
        {
            _symbol.Text = await _session.Currency.GetSymbolAsync(_code.Text ?? "", _lifetime.Token);
            _description.Text = _code.Text?.Trim().ToUpperInvariant();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception error)
        {
            _symbol.Text = "—";
            _description.Text = error.Message;
        }
        finally
        {
            _lookup.IsEnabled = !_closed;
        }
    }

    public async Task CloseAsync()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        try
        {
            _lifetime.Cancel();
            await _session.DisposeAsync();
        }
        catch (Exception error)
        {
            Console.WriteLine("CURRENCY_AVALONIA FAIL shutdown: " + error);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
