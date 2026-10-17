using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Currency.AvaloniaSample;

namespace Currency.AvaloniaIos;

public sealed class App : Application
{
    private CurrencyView? _view;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            _view = new CurrencyView(ReportResult);
            lifetime.MainView = _view;
        }
        base.OnFrameworkInitializationCompleted();
    }

    public Task CloseAsync() => _view?.CloseAsync() ?? Task.CompletedTask;

    private static void ReportResult(string result)
    {
        Console.WriteLine("CURRENCY_AVALONIA " + result);
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var destination = Path.Combine(directory, "currency-avalonia-result.txt");
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, result);
        File.Move(temporary, destination, overwrite: true);
    }
}
