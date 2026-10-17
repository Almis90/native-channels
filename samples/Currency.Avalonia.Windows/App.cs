using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Currency.AvaloniaSample;

namespace Currency.AvaloniaWindows;

public sealed class App : Application
{
    private CurrencyView? _view;
    private Window? _window;
    private bool _closing;
    private bool _disposed;
    private bool _verifyAndExit;

    internal static bool VerificationFailed { get; private set; }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _verifyAndExit = lifetime.Args?.Contains("--verify") == true;
            _view = new CurrencyView(ReportResult);
            _window = new Window
            {
                Title = "Currency Symbols — Avalonia",
                Width = 600,
                Height = 760,
                MinWidth = 360,
                MinHeight = 400,
                Content = _view,
            };
            _window.Closing += OnClosing;
            lifetime.MainWindow = _window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_disposed)
        {
            return;
        }
        args.Cancel = true;
        if (_closing)
        {
            return;
        }
        _closing = true;
        if (_view is not null)
        {
            await _view.CloseAsync();
        }
        _disposed = true;
        Dispatcher.UIThread.Post(() => _window?.Close());
    }

    private void ReportResult(string result)
    {
        VerificationFailed |= !result.StartsWith("PASS windows:", StringComparison.Ordinal);
        Console.WriteLine("CURRENCY_AVALONIA " + result);
        var destination = Environment.GetEnvironmentVariable("CURRENCY_AVALONIA_RESULT");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            var temporary = destination + ".tmp";
            File.WriteAllText(temporary, result);
            File.Move(temporary, destination, overwrite: true);
        }
        if (_verifyAndExit)
        {
            Dispatcher.UIThread.Post(() => _window?.Close());
        }
    }
}
