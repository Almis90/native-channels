using Microsoft.UI.Xaml;

namespace UnoSamples;

public partial class App : Application
{
    public App() => InitializeComponent();

    public static void InitializeLogging() { }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new Window();
        var page = new SamplePage(window);
        window.Content = page;
        var closed = false;
        window.Closed += async (_, e) =>
        {
            if (closed)
                return;
            e.Handled = true;
            try
            {
                await page.DisposeAsync();
            }
            finally
            {
                closed = true;
                window.Close();
            }
        };
        Suspending += async (_, e) =>
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                await page.SuspendAsync();
            }
            finally
            {
                deferral.Complete();
            }
        };
        EnteredBackground += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                await page.SuspendAsync();
            }
            finally
            {
                deferral.Complete();
            }
        };
        window.Activate();
    }
}
