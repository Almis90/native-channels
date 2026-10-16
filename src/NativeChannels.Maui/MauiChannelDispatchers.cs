using MauiMainThread = Microsoft.Maui.ApplicationModel.MainThread;

namespace NativeChannels;

public static class MauiChannelDispatchers
{
    public static async ValueTask<object?> MainThread(
        Func<ValueTask<object?>> handler,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await MauiMainThread
            .InvokeOnMainThreadAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await handler();
            })
            .ConfigureAwait(false);
    }
}
