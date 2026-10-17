using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Currency.AvaloniaSample;

namespace Currency.AvaloniaAndroid;

[Activity(
    Theme = "@style/CurrencyTheme",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnDestroy()
    {
        // Each recreated activity gets a new view and session. Do not block Android's UI thread
        // while the native bridge acknowledges asynchronous shutdown.
        if (Content is CurrencyView view)
        {
            _ = view.CloseAsync();
        }
        base.OnDestroy();
    }
}
