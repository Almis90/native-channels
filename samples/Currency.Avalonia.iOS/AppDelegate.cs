using Avalonia.iOS;
using Foundation;
using UIKit;

namespace Currency.AvaloniaIos;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    [Export("applicationWillTerminate:")]
    public void WillTerminate(UIApplication application)
    {
        // Shutdown is best effort: iOS may terminate a suspended process without a callback.
        // Preserve the session through background/foreground transitions; never block the UI.
        if (Avalonia.Application.Current is App app)
        {
            _ = app.CloseAsync();
        }
    }
}
