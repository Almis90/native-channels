using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace ReferenceSamples;

[Activity(
    Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SampleApplication.VerifyOnLoad = Intent?.GetBooleanExtra("verify", false) == true;
        base.OnCreate(savedInstanceState);
        (Avalonia.Application.Current as SampleApplication)?.EnsureView();
    }

    protected override void OnPause()
    {
        _ = (Avalonia.Application.Current as SampleApplication)?.SuspendAsync();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _ = (Avalonia.Application.Current as SampleApplication)?.CloseViewAsync();
        base.OnDestroy();
    }
}
