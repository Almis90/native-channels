using Xunit;

namespace NativeChannels.Tests;

public sealed class ViewChannelLeaseTests
{
    [Fact]
    public async Task DetachInvalidatesChannelAndCancelsPendingNativeWork()
    {
        var (managed, native) = InMemoryTransport.CreatePair();
        await using var remote = new ChannelService(new BinaryMessenger(native));
        var lease = new ViewChannelLease(managed, new(), TimeSpan.FromSeconds(30), new());
        using var handler = remote.GetMethodChannel(lease.Channel.Name);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.SetMethodCallHandler(
            async (_, token) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cancelled.TrySetResult();
                }
                return null;
            }
        );
        var stale = lease.Channel;
        var request = stale.InvokeAsync("pending");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await lease.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stale.InvokeAsync("late"));
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task InstancesAndReconnectsHaveIndependentEndpoints()
    {
        var (firstManaged, firstNative) = InMemoryTransport.CreatePair();
        var (secondManaged, secondNative) = InMemoryTransport.CreatePair();
        await using var firstRemote = new ChannelService(new BinaryMessenger(firstNative));
        await using var secondRemote = new ChannelService(new BinaryMessenger(secondNative));
        await using var first = new ViewChannelLease(firstManaged, new(), TimeSpan.FromSeconds(30), new());
        await using var second = new ViewChannelLease(secondManaged, new(), TimeSpan.FromSeconds(30), new());
        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.NotEqual(first.Channel.Name, second.Channel.Name);
        using var server = secondRemote.GetMethodChannel(second.Channel.Name);
        server.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>("still connected"));
        await first.DisposeAsync();
        Assert.Equal("still connected", await second.Channel.InvokeAsync<string>("read"));
        var (nextManaged, nextNative) = InMemoryTransport.CreatePair();
        await using var next = new ViewChannelLease(nextManaged, new(), TimeSpan.FromSeconds(30), new());
        await nextNative.DisposeAsync();
        Assert.NotEqual(first.InstanceId, next.InstanceId);
        Assert.NotEqual(first.Channel.Name, next.Channel.Name);
    }

    [Fact]
    public async Task IncomingCallbacksUseConfiguredDispatcherAndRegistrationOwnership()
    {
        var (managed, native) = InMemoryTransport.CreatePair();
        await using var remote = new ChannelService(new BinaryMessenger(native));
        var dispatched = false;
        await using var lease = new ViewChannelLease(
            managed,
            new(),
            TimeSpan.FromSeconds(30),
            new(),
            (work, _) =>
            {
                dispatched = true;
                return work();
            }
        );
        using var caller = remote.GetMethodChannel(lease.Channel.Name);
        using (lease.Channel.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>("clicked")))
            Assert.Equal("clicked", await caller.InvokeAsync<string>("clicked"));
        Assert.True(dispatched);
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => caller.InvokeAsync("clicked"));
    }
}
