using Avalonia.Threading;

namespace NativeChannels;

/// <summary>Dispatchers for incoming method and basic-message handlers.</summary>
public static class AvaloniaChannelDispatchers
{
    /// <summary>Queues handler starts on Avalonia's UI thread without blocking it on asynchronous replies.</summary>
    public static async ValueTask<object?> UIThread(
        Func<ValueTask<object?>> handler,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher
            .UIThread.InvokeAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await handler();
            })
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
