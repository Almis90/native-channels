using Android.App;
using Android.Content.PM;
using Microsoft.Extensions.DependencyInjection;

namespace AudioStreamer.Sample;

[Activity(
    Theme = "@style/AudioStreamer.MainTheme",
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
    private bool _isTopResumed;

    public override void OnTopResumedActivityChanged(bool isTopResumedActivity)
    {
        base.OnTopResumedActivityChanged(isTopResumedActivity);
        _isTopResumed = isTopResumedActivity;
        VerifyWhenForeground();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            VerifyWhenForeground();
    }

    private void VerifyWhenForeground()
    {
        if (!_isTopResumed || !HasWindowFocus)
            return;
        // Top-resumed can precede the first visible window. Let Android finish
        // both callbacks before requesting audio focus from the native player.
        Window!.DecorView.Post(async () =>
        {
            if (_isTopResumed && HasWindowFocus)
            {
                Console.WriteLine("AUDIO_STARTUP top-resumed and window focused");
                await IPlatformApplication.Current!.Services.GetRequiredService<MainPage>().VerifyPlaybackAsync();
            }
        });
    }
}
