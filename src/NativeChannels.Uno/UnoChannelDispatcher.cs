using Microsoft.UI.Dispatching;

namespace NativeChannels;

/// <summary>A dispatcher bound to one Uno window's UI queue.</summary>
public sealed class UnoChannelDispatcher : IHostDispatcher
{
    private readonly DispatcherQueue _queue;

    public UnoChannelDispatcher(DispatcherQueue queue) =>
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public void VerifyAccess()
    {
        if (!HasThreadAccess)
            throw new InvalidOperationException("Use the owning Uno window's UI thread.");
    }

    public async ValueTask<object?> DispatchAsync(Func<ValueTask<object?>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        if (
            !_queue.TryEnqueue(async () =>
            {
                if (completion.Task.IsCompleted)
                    return;
                try
                {
                    completion.TrySetResult(await handler());
                }
                catch (OperationCanceledException error)
                {
                    completion.TrySetCanceled(error.CancellationToken);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            })
        )
            completion.TrySetException(
                new InvalidOperationException(
                    "The Uno UI dispatcher is shutting down. Dispose channel sessions before closing the window."
                )
            );
        return await completion.Task.ConfigureAwait(false);
    }

    async Task IHostDispatcher.InvokeAsync(Func<Task> action) =>
        await DispatchAsync(
            async () =>
            {
                await action();
                return null;
            },
            default
        );
}
