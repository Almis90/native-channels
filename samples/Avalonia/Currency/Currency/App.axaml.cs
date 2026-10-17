using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Currency.Services;
using Currency.Views;

namespace Currency;

public partial class App : Avalonia.Application
{
    public static Func<CurrencySession>? SessionFactory { get; set; }
    public static Action<string>? ResultReported { get; set; }
    public static bool ExitAfterVerification { get; set; }
    private MainView? _view;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Task? _shutdown;
    private bool _shutdownComplete;
    private int _exitCode;

    public App() => Name = "Currency Symbols";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.MainWindow = new MainWindow { Content = CreateView() };
            desktop.MainWindow.Closing += (_, args) =>
            {
                if (_shutdownComplete)
                    return;
                args.Cancel = true;
                _ = CloseAsync();
            };
            desktop.ShutdownRequested += (_, args) =>
            {
                if (_shutdownComplete)
                    return;
                args.Cancel = true;
                _ = CloseAsync();
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activities)
        {
            activities.MainViewFactory = CreateView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = CreateView();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private Control CreateView()
    {
        if (SessionFactory is null)
        {
            // The generated Browser head is retained as a scaffold until its native adapter exists.
            return new TextBlock
            {
                Text = "Currency lookups are not available on this platform yet.",
                Margin = new Thickness(24),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
        }
        _view = new MainView(SessionFactory(), ReportResult);
        return _view;
    }

    private void ReportResult(string result)
    {
        Console.WriteLine("CURRENCY_AVALONIA " + result);
        ResultReported?.Invoke(result);
        if (!result.StartsWith("PASS ", StringComparison.Ordinal))
            _exitCode = 1;
        if (ExitAfterVerification)
            Dispatcher.UIThread.Post(() => _desktop?.MainWindow?.Close());
    }

    public Task CloseAsync() => _shutdown ??= ShutdownAsync();

    private async Task ShutdownAsync()
    {
        if (_view is not null)
            await _view.CloseAsync();
        _shutdownComplete = true;
        Console.WriteLine("CURRENCY_AVALONIA_CLOSED");
        _desktop?.Shutdown(_exitCode);
    }
}
