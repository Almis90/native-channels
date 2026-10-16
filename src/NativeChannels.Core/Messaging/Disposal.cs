namespace NativeChannels;

internal static class Disposal
{
    public static void Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException error)
        {
            System.Diagnostics.Trace.TraceError("NativeChannels cancellation callback failed: {0}", error);
        }
    }

    public static async void Observe(ValueTask cleanup)
    {
        try
        {
            await cleanup.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("NativeChannels asynchronous cleanup failed: {0}", error);
        }
    }
}
