using System.IO;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace Currency.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.SessionFactory = NativeCurrency.CreateSession;
        App.ResultReported = result =>
        {
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var destination = Path.Combine(directory, "currency-avalonia-result.txt");
            var temporary = destination + ".tmp";
            File.WriteAllText(temporary, result);
            File.Move(temporary, destination, overwrite: true);
        };
        return base.CustomizeAppBuilder(builder);
    }

    [Export("applicationWillTerminate:")]
    public void WillTerminate(UIApplication application)
    {
        // iOS termination callbacks are best effort; preserve the session while backgrounded.
        if (Avalonia.Application.Current is App app)
            _ = app.CloseAsync();
    }
}
