using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Currency.AvaloniaAndroid;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime lifetime)
        {
            lifetime.MainViewFactory = () => new CurrencyView();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
