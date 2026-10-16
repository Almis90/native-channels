using Xunit;

namespace NativeChannels.Tests;

public sealed class TransportLifecycleTests
{
    [Fact]
    public async Task ThrowingCancellationCallbackCannotInterruptTransportDisposal()
    {
        var (sender, receiver) = InMemoryTransport.CreatePair();
        await using var remote = receiver;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.SetMessageHandler(
            async (_, token) =>
            {
                using var callback = token.Register(() => throw new InvalidOperationException("Adapter failure"));
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cleaned.TrySetResult();
                }
                return new(TransportStatus.Success, default);
            }
        );
        var call = sender.SendAsync(new(Guid.NewGuid(), "cleanup", default)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            sender.SendAsync(new(Guid.NewGuid(), "cleanup", default)).AsTask()
        );
    }
}
