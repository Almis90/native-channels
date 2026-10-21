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
        var closing = false;
        window.Closed += (_, e) =>
        {
            if (closed)
                return;
            e.Handled = true;
            if (closing)
                return;
            closing = true;
            // DisposeAsync can complete synchronously. Calling Close from inside
            // the original Closed callback reenters Uno's close handling before
            // it has applied Handled, leaving the window open. Always unwind the
            // first notification before cleanup and the final close request.
            if (
                !window.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await page.DisposeAsync();
                    }
                    finally
                    {
                        closed = true;
                        window.Close();
                    }
                })
            )
                throw new InvalidOperationException("Unable to enqueue window cleanup.");
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
