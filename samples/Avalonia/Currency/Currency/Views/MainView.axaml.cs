using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Currency.Services;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;

namespace Currency.Views;

public sealed partial class MainView : UserControl
{
    private readonly CurrencySession? _session;
    private readonly AvaloniaChannelSession? _hostSession;
    private readonly Action<string> _report = result => Console.WriteLine("CURRENCY_AVALONIA " + result);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _code = new()
    {
        Text = "USD",
        PlaceholderText = "Currency code",
        MaxLength = 12,
    };
    private readonly TextBlock _symbol = new()
    {
        Text = "—",
        FontSize = 72,
        Foreground = Brush.Parse("#6554C0"),
        TextAlignment = TextAlignment.Center,
    };
    private readonly TextBlock _description = new()
    {
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
    };
    private readonly TextBlock _verification = new()
    {
        Text = "Checking the packaged native plugin…",
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _lookup = new()
    {
        Content = "Get currency symbol",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        MinHeight = 44,
    };
    private bool _started;
    private bool _closed;
    private Task? _closeTask;

    public MainView(AvaloniaChannelSession session, Action<string>? report = null)
        : this()
    {
        _hostSession = session;
        _session = session.Services.GetRequiredService<CurrencySession>();
        session.Stopping.Register(() =>
        {
            _closed = true;
            _lifetime.Cancel();
        });
        if (report is not null)
            _report = report;
    }

    // The template designer can construct the view without starting a native session.
    public MainView()
    {
        InitializeComponent();
        TextInputOptions.SetReturnKeyType(_code, TextInputReturnKeyType.Go);
        _lookup.Click += async (_, _) => await LookupAsync();
        _code.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter)
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
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            button.Click += async (_, _) =>
            {
                _code.Text = code;
                await LookupAsync();
            };
            shortcuts.Children.Add(button);
        }
        var header = new Border
        {
            Background = Brush.Parse("#2C3E50"),
            Padding = new Thickness(24, 14),
            Child = new TextBlock
            {
                Text = "Currency Symbols",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
            },
        };
        var content = new ScrollViewer
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border
            {
                Padding = new Thickness(24),
                MaxWidth = 520,
                Child = new StackPanel
                {
                    Spacing = 22,
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
                        shortcuts,
                        _symbol,
                        _description,
                        new TextBlock
                        {
                            Text =
                                "Symbols use your device’s language and region. For example, USD may appear as $ or US$.",
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        _verification,
                        new TextBlock
                        {
                            Text =
                                "Try CHF, CAD or AUD for more currencies. Enter ZZZ to see how an unsupported code is reported.",
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },
            },
        };
        Grid.SetRow(content, 1);
        Content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, content } };
        Loaded += async (_, _) => await VerifyAsync();
    }

    private async Task VerifyAsync()
    {
        if (_session is null || _started || _closed)
        {
            return;
        }
        _started = true;
        string result;
        try
        {
            result = await _session.VerifyAsync(_lifetime.Token);
            if (_closed)
            {
                return;
            }
            _verification.Text =
                "Native checks passed: lookups, input normalization, error handling, concurrent calls and cancellation.";
        }
        catch (OperationCanceledException) when (_closed)
        {
            return;
        }
        catch (ObjectDisposedException) when (_closed)
        {
            return;
        }
        catch (Exception error)
        {
            result = "FAIL " + error;
            _verification.Text = "Native checks failed: " + error.Message;
        }
        _report(result);
        await LookupAsync();
    }

    private async Task LookupAsync()
    {
        if (_session is null || _closed)
        {
            return;
        }
        _lookup.IsEnabled = false;
        try
        {
            _symbol.Text = await _session.GetSymbolAsync(_code.Text ?? "", _lifetime.Token);
            _description.Text = _code.Text?.Trim().ToUpperInvariant();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (ObjectDisposedException) when (_closed) { }
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

    public Task CloseAsync() => _closeTask ??= CloseCoreAsync();

    private async Task CloseCoreAsync()
    {
        _closed = true;
        try
        {
            try
            {
                _lifetime.Cancel();
            }
            finally
            {
                if (_hostSession is not null)
                    await _hostSession.DisposeAsync();
            }
        }
        catch (Exception error)
        {
            _report("FAIL shutdown: " + error);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
