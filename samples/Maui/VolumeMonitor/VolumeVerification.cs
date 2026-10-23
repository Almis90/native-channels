using Plugin.VolumeMonitor;

namespace VolumeMonitor.Sample;

internal static class VolumeVerification
{
    public static async Task<string> RunAsync(IVolumeMonitor monitor, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var secondCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await using var first = monitor.ListenAsync(firstCancellation.Token).GetAsyncEnumerator();
        await using var second = monitor.ListenAsync(secondCancellation.Token).GetAsyncEnumerator();
        if (!await first.MoveNextAsync() || !await second.MoveNextAsync())
            throw new InvalidOperationException("Expected an initial level for each listener.");
        firstCancellation.Cancel();
        await ExpectCancellationAsync(first);
        // The second listener remains usable when the first is removed.
        var pending = second.MoveNextAsync().AsTask();
        if (pending.IsCompleted && !await pending)
            throw new InvalidOperationException("The second listener ended when the first was cancelled.");
        secondCancellation.Cancel();
        try
        {
            await pending;
        }
        catch (OperationCanceledException) { }
        await using var restarted = monitor.ListenAsync(deadline.Token).GetAsyncEnumerator();
        if (!await restarted.MoveNextAsync())
            throw new InvalidOperationException("Expected an initial level after restarting.");
        return "PASS initial levels, independent subscriptions, cancellation and restart; hardware volume changes require separate verification";
    }

    private static async Task ExpectCancellationAsync(IAsyncEnumerator<double> listener)
    {
        try
        {
            await listener.MoveNextAsync();
            throw new InvalidOperationException("Cancelled listener continued.");
        }
        catch (OperationCanceledException) { }
    }
}
