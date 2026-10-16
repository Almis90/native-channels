using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Xunit;

namespace NativeChannels.Tests;

public sealed class MessageAndEventTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static (ChannelService Client, ChannelService Server, BinaryMessenger Messenger) Pair()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        var messenger = new BinaryMessenger(a);
        return (new(messenger), new(new BinaryMessenger(b)), messenger);
    }

    [Fact]
    public void ByteAndStringCodecsPreserveNullEmptyUnicodeAndRejectMalformedData()
    {
        IMessageCodec[] codecs = [BinaryMessageCodec.Instance, StringMessageCodec.Instance];
        foreach (var codec in codecs)
        {
            Assert.Null(codec.Decode(codec.Encode(null)));
            Assert.Throws<ChannelSerializationException>(() => codec.Decode(Array.Empty<byte>()));
            Assert.Throws<ChannelSerializationException>(() => codec.Decode(new byte[] { 0, 1 }));
            Assert.Throws<ChannelSerializationException>(() => codec.Decode(new byte[] { 2 }));
            Assert.Throws<ChannelSerializationException>(() => codec.Encode(42));
        }
        Assert.Equal("", StringMessageCodec.Instance.Decode(new byte[] { 1 }));
        Assert.Equal("€ 🌍", StringMessageCodec.Instance.Decode(StringMessageCodec.Instance.Encode("€ 🌍")));
        Assert.Throws<ChannelSerializationException>(() => StringMessageCodec.Instance.Decode(new byte[] { 1, 0xff }));
        Assert.Throws<ChannelSerializationException>(() => StringMessageCodec.Instance.Encode("\ud800"));
        var bytes = new byte[] { 0, 255, 1 };
        var encoded = BinaryMessageCodec.Instance.Encode(bytes);
        bytes[0] = 99;
        Assert.Equal(new byte[] { 0, 255, 1 }, (byte[])BinaryMessageCodec.Instance.Decode(encoded)!);
        Assert.Empty((byte[])BinaryMessageCodec.Instance.Decode(new byte[] { 1 })!);
    }

    [Fact]
    public async Task BasicChannelsRoundTripAllCodecsAndAllowReentrantReplies()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        IMessageCodec[] codecs = [new JsonMessageCodec(), StringMessageCodec.Instance, BinaryMessageCodec.Instance];
        object?[] values = [long.MaxValue, "hello €", new byte[] { 0, 255 }];
        for (var i = 0; i < codecs.Length; i++)
        {
            var options = new BasicMessageChannelOptions { Codec = codecs[i] };
            using var outgoing = client.GetBasicMessageChannel("echo", options);
            using var incoming = server.GetBasicMessageChannel("echo", options);
            using var local = outgoing.SetMessageHandler((value, _) => ValueTask.FromResult(value));
            using var remote = incoming.SetMessageHandler(async (value, ct) => await incoming.SendAsync(value, ct));
            Assert.Equal(values[i], await outgoing.SendAsync(values[i]));
            Assert.Null(await outgoing.SendAsync(null));
        }
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task BasicOwnershipTimeoutAndKindConflicts()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        using var first = server.GetBasicMessageChannel("shared");
        using var second = server.GetBasicMessageChannel("shared");
        var old = first.SetMessageHandler((value, _) => ValueTask.FromResult(value));
        Assert.Throws<InvalidOperationException>(() => second.SetMessageHandler((v, _) => ValueTask.FromResult(v)));
        old.Dispose();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var current = second.SetMessageHandler(
            async (value, ct) =>
            {
                if (value is string text && text == "wait")
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                return value;
            }
        );
        first.Dispose();
        old.Dispose();
        using var caller = client.GetBasicMessageChannel("shared", new() { Timeout = TimeSpan.FromMilliseconds(20) });
        Assert.Equal(42, await caller.SendAsync<int>(42, options: new() { Timeout = Timeout.InfiniteTimeSpan }));
        await Assert.ThrowsAsync<TimeoutException>(() => caller.SendAsync("wait"));
        using var cancellation = new CancellationTokenSource();
        var infinite = caller.SendAsync("wait", cancellation.Token, new() { Timeout = Timeout.InfiniteTimeSpan });
        await entered.Task.WaitAsync(Deadline);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => infinite);
        Assert.Throws<InvalidOperationException>(() => client.GetMethodChannel("shared"));
        Assert.Throws<InvalidOperationException>(() => client.GetEventChannel("shared"));
        Assert.Throws<InvalidOperationException>(() =>
            client.GetBasicMessageChannel("shared", new() { Codec = StringMessageCodec.Instance })
        );
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task NullEventAndCompletionAreDistinct()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        using var source = server.GetEventChannel("events");
        using var registration = source.SetStreamHandler(NullThenValue);
        using var events = client.GetEventChannel("events");
        var values = new List<string?>();
        using var deadline = new CancellationTokenSource(Deadline);
        await foreach (var value in events.ListenAsync<string>().WithCancellation(deadline.Token))
            values.Add(value);
        Assert.Equal(new string?[] { null, "value" }, values);
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    private static async IAsyncEnumerable<object?> NullThenValue(
        object? _,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        await Task.Yield();
        yield return null;
        yield return "value";
    }

    [Theory]
    [InlineData(EventOverflowPolicy.Fail, 1)]
    [InlineData(EventOverflowPolicy.DropOldest, 3)]
    [InlineData(EventOverflowPolicy.DropNewest, 1)]
    public async Task OverflowPoliciesBoundBuffersAndAlwaysDeliverTerminal(EventOverflowPolicy policy, int expected)
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<object?> Produce(object? _, [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                yield return 0;
                await release.Task.WaitAsync(ct);
                yield return 1;
                yield return 2;
                yield return 3;
            }
            finally
            {
                stopped.TrySetResult();
            }
        }
        using var source = server.GetEventChannel("events");
        using var handler = source.SetStreamHandler(Produce);
        using var events = client.GetEventChannel("events", new() { BufferCapacity = 1, OverflowPolicy = policy });
        await using var iterator = events.ListenAsync<int>().GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(0, iterator.Current);
        release.SetResult();
        await stopped.Task.WaitAsync(Deadline);
        Assert.True(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(expected, iterator.Current);
        if (policy == EventOverflowPolicy.Fail)
            await Assert.ThrowsAsync<EventBufferOverflowException>(() =>
                iterator.MoveNextAsync().AsTask().WaitAsync(Deadline)
            );
        else
            Assert.False(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
    }

    [Fact]
    public async Task ErrorAfterFullBufferPreservesDetails()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<object?> Produce(object? _, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return 0;
            await release.Task.WaitAsync(ct);
            yield return 1;
            stopped.SetResult();
            throw new ChannelException("sensor_error", "Disconnected", 123L, "android");
        }
        using var source = server.GetEventChannel("events");
        using var handler = source.SetStreamHandler(Produce);
        using var events = client.GetEventChannel("events", new() { BufferCapacity = 1 });
        await using var iterator = events.ListenAsync<int>().GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
        release.SetResult();
        await stopped.Task.WaitAsync(Deadline);
        Assert.True(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(1, iterator.Current);
        var error = await Assert.ThrowsAsync<ChannelException>(() =>
            iterator.MoveNextAsync().AsTask().WaitAsync(Deadline)
        );
        Assert.Equal("sensor_error", error.Code);
        Assert.Equal(123L, error.Details);
        Assert.Equal("android", error.Platform);
    }

    [Fact]
    public async Task EachEnumerationOwnsSubscriptionAndCancellationCleanup()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        var count = 0;
        var cleaned = Channel.CreateUnbounded<int>();
        async IAsyncEnumerable<object?> Produce(object? _, [EnumeratorCancellation] CancellationToken ct)
        {
            var id = Interlocked.Increment(ref count);
            try
            {
                yield return id;
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                cleaned.Writer.TryWrite(id);
            }
        }
        using var source = server.GetEventChannel("events");
        using var handler = source.SetStreamHandler(Produce);
        using var first = client.GetEventChannel("events");
        using var second = client.GetEventChannel("events");
        var enumerable = first.ListenAsync<int>();
        await using var a = enumerable.GetAsyncEnumerator();
        await using var b = enumerable.GetAsyncEnumerator();
        await using var c = second.ListenAsync<int>().GetAsyncEnumerator();
        Assert.True(await a.MoveNextAsync().AsTask().WaitAsync(Deadline));
        Assert.True(await b.MoveNextAsync().AsTask().WaitAsync(Deadline));
        Assert.True(await c.MoveNextAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(new[] { 1, 2, 3 }, new[] { a.Current, b.Current, c.Current });
        await a.DisposeAsync();
        Assert.Equal(1, await cleaned.Reader.ReadAsync().AsTask().WaitAsync(Deadline));
        var pendingB = b.MoveNextAsync().AsTask();
        await first.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingB.WaitAsync(Deadline));
        Assert.Equal(2, await cleaned.Reader.ReadAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(1, pair.Messenger.PendingRequestCount);
        await c.DisposeAsync();
        Assert.Equal(3, await cleaned.Reader.ReadAsync().AsTask().WaitAsync(Deadline));
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task EventRegistrationTokensOwnOnlyTheirHandlerAndDisposalCancelsProducers()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<object?> Produce(object? _, [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                yield return 1;
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                stopped.TrySetResult();
            }
        }
        using var first = server.GetEventChannel("events");
        using var second = server.GetEventChannel("events");
        var old = first.SetStreamHandler(Produce);
        Assert.Throws<InvalidOperationException>(() => second.SetStreamHandler(Produce));
        old.Dispose();
        using var current = second.SetStreamHandler(Produce);
        first.Dispose();
        old.Dispose();
        using var events = client.GetEventChannel("events");
        await using var iterator = events.ListenAsync<int>().GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync().AsTask().WaitAsync(Deadline));
        var pending = iterator.MoveNextAsync().AsTask();
        await client.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(Deadline));
        await stopped.Task.WaitAsync(Deadline);
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task InvalidTypedEventUnsubscribesAndStreamsIgnoreRequestTimeouts()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        await using var client = new ChannelService(new BinaryMessenger(a), timeout: TimeSpan.FromMilliseconds(1));
        await using var server = new ChannelService(new BinaryMessenger(b));
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<object?> Produce(object? _, [EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                await Task.Delay(30, ct);
                yield return null;
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                stopped.TrySetResult();
            }
        }
        using var source = server.GetEventChannel("events");
        using var handler = source.SetStreamHandler(Produce);
        using var events = client.GetEventChannel("events");
        await using var iterator = events.ListenAsync<int>().GetAsyncEnumerator();
        await Assert.ThrowsAsync<ChannelSerializationException>(() =>
            iterator.MoveNextAsync().AsTask().WaitAsync(Deadline)
        );
        await stopped.Task.WaitAsync(Deadline);
    }

    [Fact]
    public async Task MissingCancelledDisposedAndInvalidSubscriptions()
    {
        var pair = Pair();
        await using var client = pair.Client;
        await using var server = pair.Server;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            client.GetEventChannel("events", new() { BufferCapacity = 0 })
        );
        Assert.Throws<ArgumentException>(() => client.GetMethodChannel("__maui.events/reserved"));
        using var events = client.GetEventChannel("events");
        await using var missing = events.ListenAsync<int>().GetAsyncEnumerator();
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => missing.MoveNextAsync().AsTask());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await using var iterator = events.ListenAsync<int>(cancellationToken: cancelled.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => iterator.MoveNextAsync().AsTask());
        events.Dispose();
        await using var disposed = events.ListenAsync<int>().GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => disposed.MoveNextAsync().AsTask());
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }
}
