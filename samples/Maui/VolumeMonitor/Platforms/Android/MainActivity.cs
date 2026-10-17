using Android.App;
using Android.Content.PM;

namespace VolumeMonitor.Sample;

[Activity(
    Theme = "@style/VolumeMonitor.MainTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density
)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        VolumeControlStream = Android.Media.Stream.Music;
    }
}
