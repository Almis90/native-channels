using NativeChannels;

namespace ReferenceSamples;

internal static class BrowserVolumeVerification
{
    public static async Task<string> RunAsync(IChannelService channels, CancellationToken stopping)
    {
        await using var events = channels.GetEventChannel("example.browser.volume");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        stop.CancelAfter(TimeSpan.FromSeconds(10));
        await using var listener = events.ListenAsync<double>(cancellationToken: stop.Token).GetAsyncEnumerator();
        if (!await listener.MoveNextAsync())
            throw new InvalidOperationException("Missing initial page volume.");
        using var control = channels.GetMethodChannel("example.browser.volume.control");
        var expected = listener.Current < 0.5 ? 0.75 : 0.25;
        var next = listener.MoveNextAsync().AsTask();
        await control.InvokeAsync("setVolume", expected, stop.Token);
        if (!await next || Math.Abs(listener.Current - expected) > 0.001)
            throw new InvalidOperationException("Page volume change was not delivered.");
        return "PASS page-owned volume initial value and change; browsers do not expose OS volume";
    }
}
