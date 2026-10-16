using System.Runtime.CompilerServices;
using Xunit;

namespace NativeChannels.Tests;

[Trait("Category", "Stress")]
public sealed class LifecycleStressTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ConcurrentReentrantCallsKeepRepliesAndHandleOwnershipIsolated()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        var outgoing = new BinaryMessenger(a);
        var incoming = new BinaryMessenger(b);
        await using var client = new ChannelService(outgoing);
        await using var server = new ChannelService(incoming);
        using var callbacks = client.GetMethodChannel("stress.echo");
        using var callback = callbacks.SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));
        using var endpoint = server.GetMethodChannel("stress.echo");
        using var handler = endpoint.SetMethodCallHandler(
            async (call, ct) => await endpoint.InvokeAsync<int>("reply", call.Arguments, ct)
        );

        for (var batch = 0; batch < 16; batch++)
        {
            var start = batch * 64;
            await Task.WhenAll(
                    Enumerable
                        .Range(start, 64)
                        .Select(async value =>
                        {
                            using var handle = client.GetMethodChannel("stress.echo");
                            Assert.Equal(value, await handle.InvokeAsync<int>("echo", value));
                        })
                )
                .WaitAsync(Deadline);
            Assert.Equal(0, outgoing.PendingRequestCount);
            Assert.Equal(0, incoming.PendingRequestCount);
        }
    }

    [Fact]
    public async Task HandlerChurnCannotRemoveReplacementOrLeakEndpointConfiguration()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        await using var client = new ChannelService(new BinaryMessenger(a));
        await using var server = new ChannelService(new BinaryMessenger(b));
        for (var generation = 0; generation < 200; generation++)
        {
            using (var old = server.GetMethodChannel("stress.churn"))
            using (var current = server.GetMethodChannel("stress.churn"))
            using (var caller = client.GetMethodChannel("stress.churn"))
            {
                var stale = old.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(-1));
                stale.Dispose();
                using var registration = current.SetMethodCallHandler(
                    (_, _) => ValueTask.FromResult<object?>(generation)
                );
                await Task.WhenAll(Task.Run(old.Dispose), Task.Run(stale.Dispose)).WaitAsync(Deadline);
                Assert.Equal(generation, await caller.InvokeAsync<int>("read").WaitAsync(Deadline));
            }
            // Reusing the name with another channel kind proves all prior handles released the endpoint.
            var options = new BasicMessageChannelOptions { Codec = BinaryMessageCodec.Instance };
            using var sender = client.GetBasicMessageChannel("stress.churn", options);
            using var receiver = server.GetBasicMessageChannel("stress.churn", options);
            using var echo = receiver.SetMessageHandler((value, _) => ValueTask.FromResult(value));
            Assert.Equal(new byte[] { 1, 2 }, await sender.SendAsync<byte[]>(new byte[] { 1, 2 }).WaitAsync(Deadline));
        }
    }

    [Fact]
    public async Task ConcurrentDisposalCancelsEveryPendingCallAndRemoteHandler()
    {
        for (var round = 0; round < 10; round++)
        {
            const int count = 64;
            var (a, b) = InMemoryTransport.CreatePair();
            var messenger = new BinaryMessenger(a);
            await using var client = new ChannelService(messenger, timeout: Timeout.InfiniteTimeSpan);
            await using var server = new ChannelService(new BinaryMessenger(b));
            using var endpoint = server.GetMethodChannel("stress.dispose");
            var entered = NewSignal();
            var cleaned = NewSignal();
            var entries = 0;
            var exits = 0;
            using var handler = endpoint.SetMethodCallHandler(
                async (_, token) =>
                {
                    if (Interlocked.Increment(ref entries) == count)
                        entered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    finally
                    {
                        if (Interlocked.Increment(ref exits) == count)
                            cleaned.TrySetResult();
                    }
                    return null;
                }
            );
            var handles = Enumerable.Range(0, count).Select(_ => client.GetMethodChannel("stress.dispose")).ToArray();
            var calls = handles.Select(handle => handle.InvokeAsync("wait")).ToArray();
            await entered.Task.WaitAsync(Deadline);
            await Task.WhenAll(
                    Task.Run(async () => await client.DisposeAsync()),
                    Task.Run(async () => await client.DisposeAsync()),
                    Task.Run(() =>
                    {
                        foreach (var handle in handles)
                            handle.Dispose();
                    })
                )
                .WaitAsync(Deadline);
            foreach (var call in calls)
                await Assert.ThrowsAsync<ObjectDisposedException>(() => call.WaitAsync(Deadline));
            await cleaned.Task.WaitAsync(Deadline);
            Assert.Equal(count, exits);
            Assert.Equal(0, messenger.PendingRequestCount);
        }
    }

    [Fact]
    public async Task RepeatedIndependentSubscriptionsUnsubscribeAndReleaseRemoteIterators()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        var outgoing = new BinaryMessenger(a);
        var incoming = new BinaryMessenger(b);
        await using var client = new ChannelService(outgoing);
        await using var server = new ChannelService(incoming);
        using var producer = server.GetEventChannel("stress.events");
        using var consumer = client.GetEventChannel("stress.events");
        var cleaned = Enumerable.Range(0, 320).Select(_ => NewSignal()).ToArray();
        using var registration = producer.SetStreamHandler((value, ct) => Stream((int)value!, ct));

        async IAsyncEnumerable<object?> Stream(int id, [EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                yield return id;
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                cleaned[id].TrySetResult();
            }
        }

        for (var batch = 0; batch < 10; batch++)
        {
            await Task.WhenAll(
                    Enumerable
                        .Range(batch * 32, 32)
                        .Select(async id =>
                        {
                            using var cancellation = new CancellationTokenSource();
                            await using (
                                var iterator = consumer.ListenAsync<int>(id, cancellation.Token).GetAsyncEnumerator()
                            )
                            {
                                Assert.True(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
                                Assert.Equal(id, iterator.Current);
                                if (id % 2 == 0)
                                {
                                    cancellation.Cancel();
                                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                                        iterator.MoveNextAsync().AsTask()
                                    );
                                }
                            }
                            await cleaned[id].Task.WaitAsync(Deadline);
                        })
                )
                .WaitAsync(Deadline);
            Assert.Equal(0, outgoing.PendingRequestCount);
        }
        // Disposing the service also waits for local request completion; remote iterator cleanup was checked above.
        await server.DisposeAsync();
        Assert.Equal(0, incoming.PendingRequestCount);
    }

    [Fact]
    public async Task RepeatedViewConnectionsInvalidateStaleChannels()
    {
        var identities = new HashSet<string>();
        for (var generation = 0; generation < 200; generation++)
        {
            var (a, b) = InMemoryTransport.CreatePair();
            await using var remote = new ChannelService(new BinaryMessenger(b));
            await using var lease = new ViewChannelLease(a, new(), Timeout.InfiniteTimeSpan, new());
            Assert.True(identities.Add(lease.InstanceId));
            using var endpoint = remote.GetMethodChannel(lease.Channel.Name);
            using var handler = endpoint.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(generation));
            Assert.Equal(generation, await lease.Channel.InvokeAsync<int>("read").WaitAsync(Deadline));
            var stale = lease.Channel;
            await lease.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => stale.InvokeAsync("read"));
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
