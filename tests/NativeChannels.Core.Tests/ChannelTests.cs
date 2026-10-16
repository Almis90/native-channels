using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace NativeChannels.Tests;

public sealed class ChannelTests
{
    private static (ChannelService Managed, ChannelService Native, BinaryMessenger Messenger) Pair(
        TimeSpan? timeout = null,
        ChannelSerializer? serializer = null
    )
    {
        var transports = InMemoryTransport.CreatePair();
        var messenger = new BinaryMessenger(transports.Managed);
        return (
            new(messenger, serializer, timeout),
            new(new BinaryMessenger(transports.Native), serializer),
            messenger
        );
    }

    [Fact]
    public async Task TypedArgumentsResultsNullAndVoid()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var handler = native.GetMethodChannel("math");
        using var registration = handler.SetMethodCallHandler(
            (call, _) =>
                ValueTask.FromResult<object?>(
                    call.Method == "null" ? null : call.GetArgument<int>("a") + call.GetArgument<int>("b")
                )
        );
        using var client = managed.GetMethodChannel("math");
        Assert.Equal(
            30,
            await client.InvokeAsync<int>("add", new Dictionary<string, object?> { ["a"] = 10, ["b"] = 20 })
        );
        Assert.Null(await client.InvokeAsync<string>("null"));
        Assert.Null(await client.InvokeAsync<int?>("null"));
        await client.InvokeAsync("null");
        await Assert.ThrowsAsync<ChannelSerializationException>(() => client.InvokeAsync<int>("null"));
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task ErrorsAreDistinctAndUnexpectedExceptionsAreSanitized()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var server = native.GetMethodChannel("errors");
        using var registration = server.SetMethodCallHandler(
            (call, _) =>
                throw call.Method switch
                {
                    "unknown" => new MethodNotImplementedException(call.Method),
                    "platform" => new ChannelException("declined", "Declined", 42L, "android"),
                    _ => new InvalidOperationException("secret must not cross the boundary"),
                }
        );
        using var client = managed.GetMethodChannel("errors");
        await Assert.ThrowsAsync<MethodNotImplementedException>(() => client.InvokeAsync("unknown"));
        var error = await Assert.ThrowsAsync<ChannelException>(() => client.InvokeAsync("platform"));
        Assert.Equal("declined", error.Code);
        Assert.Equal(42L, error.Details);
        Assert.Equal("android", error.Platform);
        var sanitized = await Assert.ThrowsAsync<ChannelException>(() => client.InvokeAsync("unexpected"));
        Assert.DoesNotContain("secret", sanitized.Message);
        using var missing = managed.GetMethodChannel("missing");
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => missing.InvokeAsync("anything"));
    }

    [Fact]
    public async Task ConcurrentRequestsEnterInOrderAndCompleteIndependently()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        var order = new List<int>();
        var releases = Enumerable
            .Range(0, 50)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = native.GetMethodChannel("parallel");
        using var registration = server.SetMethodCallHandler(
            async (call, ct) =>
            {
                var index = call.GetArguments<int>();
                order.Add(index);
                if (order.Count == releases.Length)
                {
                    entered.SetResult();
                }

                await releases[index].Task.WaitAsync(ct);
                return index;
            }
        );
        using var client = managed.GetMethodChannel("parallel");
        var tasks = Enumerable.Range(0, 50).Select(i => client.InvokeAsync<int>("echo", i)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Enumerable.Range(0, 50), order);
        releases[49].SetResult();
        Assert.Equal(49, await tasks[49].WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(tasks[0].IsCompleted);
        for (var i = 48; i >= 0; i--)
        {
            releases[i].SetResult();
        }

        Assert.Equal(Enumerable.Range(0, 50), await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task ReentrantCallbacksDoNotDeadlock()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var client = managed.GetMethodChannel("reentrant");
        using var server = native.GetMethodChannel("reentrant");
        using var local = client.SetMethodCallHandler(
            (call, _) => ValueTask.FromResult<object?>(call.GetArguments<int>() + 1)
        );
        using var remote = server.SetMethodCallHandler(
            async (call, ct) => await server.InvokeAsync<int>("callback", call.Arguments, ct)
        );
        Assert.Equal(8, await client.InvokeAsync<int>("invoke", 7).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TimeoutCancellationAndInfiniteOverride()
    {
        var pair = Pair(TimeSpan.FromMilliseconds(30));
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var server = native.GetMethodChannel("wait");
        using var registration = server.SetMethodCallHandler(
            async (_, ct) =>
            {
                await Task.Delay(100, ct);
                return "done";
            }
        );
        using var client = managed.GetMethodChannel("wait");
        await Assert.ThrowsAsync<TimeoutException>(() => client.InvokeAsync("wait"));
        Assert.Equal(
            "done",
            await client.InvokeAsync<string>("wait", options: new() { Timeout = Timeout.InfiniteTimeSpan })
        );
        using var cancellation = new CancellationTokenSource();
        var pending = client.InvokeAsync(
            "wait",
            cancellationToken: cancellation.Token,
            options: new() { Timeout = Timeout.InfiniteTimeSpan }
        );
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task TimeoutPrecedenceAndValidation()
    {
        var pair = Pair(TimeSpan.FromMilliseconds(1));
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var server = native.GetMethodChannel("timeout");
        using var registration = server.SetMethodCallHandler(
            async (_, ct) =>
            {
                await Task.Delay(60, ct);
                return true;
            }
        );
        using var client = managed.GetMethodChannel("timeout", new() { Timeout = Timeout.InfiniteTimeSpan });
        Assert.True(await client.InvokeAsync<bool>("wait"));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.InvokeAsync("wait", options: new() { Timeout = TimeSpan.FromMilliseconds(1) })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            managed.GetMethodChannel("bad", new() { Timeout = TimeSpan.Zero })
        );
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.InvokeAsync("bad", options: new() { Timeout = TimeSpan.FromMilliseconds(-2) })
        );
    }

    [Fact]
    public async Task HandleDisposalIsIsolatedAndPendingCallsComplete()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var server = native.GetMethodChannel("shared");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = server.SetMethodCallHandler(
            async (call, ct) =>
            {
                if (call.Method == "wait")
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                return 7;
            }
        );
        using var first = managed.GetMethodChannel("shared");
        using var second = managed.GetMethodChannel("shared");
        var pending = first.InvokeAsync("wait");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Equal(7, await second.InvokeAsync<int>("value"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.InvokeAsync("value"));
    }

    [Fact]
    public async Task RegistrationsHaveOwnershipAndConflictsAreRejected()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var first = managed.GetMethodChannel("shared");
        using var second = managed.GetMethodChannel("shared");
        var old = first.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(1));
        Assert.Throws<InvalidOperationException>(() =>
            second.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(2))
        );
        old.Dispose();
        using var current = second.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(2));
        old.Dispose();
        first.Dispose();
        using var caller = native.GetMethodChannel("shared");
        Assert.Equal(2, await caller.InvokeAsync<int>("call"));
        Assert.Throws<InvalidOperationException>(() =>
            managed.GetMethodChannel("shared", new() { Codec = new JsonMethodCodec() })
        );
        current.Dispose();
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => caller.InvokeAsync("call"));
    }

    [Fact]
    public async Task MessengerDisposalCompletesPendingCalls()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = native.GetMethodChannel("wait");
        using var registration = server.SetMethodCallHandler(
            async (_, ct) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            }
        );
        using var client = managed.GetMethodChannel("wait");
        var pending = client.InvokeAsync("wait");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await managed.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public void TypedDtoAndCollectionConversionsRejectPrecisionLoss()
    {
        var serializer = new ChannelSerializer(
            new(JsonSerializerDefaults.Web) { TypeInfoResolver = TestJsonContext.Default }
        );
        Assert.Throws<ChannelSerializationException>(() => serializer.ConvertResult<double>(long.MaxValue));
        Assert.Throws<ChannelSerializationException>(() =>
            serializer.ConvertResult<FloatingDto>(new Dictionary<string, object?> { ["value"] = long.MaxValue })
        );
        Assert.Throws<ChannelSerializationException>(() =>
            serializer.ConvertResult<List<double>>(new List<object?> { long.MaxValue })
        );
        Assert.Equal(42d, serializer.ConvertResult<double>(42L));
        Assert.Equal(42L, serializer.ConvertResult<long>(42));
        Assert.Equal(42, serializer.ConvertResult<int>(42L));
    }

    [Fact]
    public async Task SourceGeneratedDtoRoundTrips()
    {
        var serializer = new ChannelSerializer(
            new(JsonSerializerDefaults.Web) { TypeInfoResolver = TestJsonContext.Default }
        );
        var pair = Pair(serializer: serializer);
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var server = native.GetMethodChannel("dto");
        using var registration = server.SetMethodCallHandler(
            (call, _) => ValueTask.FromResult<object?>(call.GetArguments<PaymentResult>())
        );
        using var client = managed.GetMethodChannel("dto");
        var result = await client.InvokeAsync<PaymentResult>("echo", new PaymentResult("accepted", long.MaxValue));
        Assert.Equal(new PaymentResult("accepted", long.MaxValue), result);
    }

    [Fact]
    public async Task LateReplyAfterCancellationCannotCorruptNextRequest()
    {
        var transport = new DelayedTransport();
        await using var messenger = new BinaryMessenger(transport);
        using var cancellation = new CancellationTokenSource();
        var cancelled = messenger
            .SendAsync("raw", new byte[] { 1 }, Timeout.InfiniteTimeSpan, cancellation.Token)
            .AsTask();
        var firstId = transport.LastRequest.Id;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var next = messenger.SendAsync("raw", new byte[] { 2 }, Timeout.InfiniteTimeSpan).AsTask();
        Assert.NotEqual(firstId, transport.LastRequest.Id);
        transport.Complete(firstId, new byte[] { 9 });
        transport.Complete(transport.LastRequest.Id, new byte[] { 2 });
        Assert.Equal(new byte[] { 2 }, (await next).ToArray());
        Assert.Equal(0, messenger.PendingRequestCount);
    }

    [Fact]
    public async Task TerminalRacesAlwaysRemovePendingState()
    {
        var transport = new DelayedTransport();
        var clock = new ControlledTimeProvider();
        await using var messenger = new BinaryMessenger(transport, timeProvider: clock);
        for (var index = 0; index < 100; index++)
        {
            using var cancellation = new CancellationTokenSource();
            var pending = messenger
                .SendAsync("race", new byte[] { 1 }, TimeSpan.FromMilliseconds(1), cancellation.Token)
                .AsTask();
            var id = transport.LastRequest.Id;
            Assert.NotEqual(Guid.Empty, id);
            await Task.WhenAll(
                Task.Run(cancellation.Cancel),
                Task.Run(clock.Fire),
                Task.Run(() => transport.Complete(id, new byte[] { 1 }))
            );
            try
            {
                Assert.Equal(new byte[] { 1 }, (await pending).ToArray());
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            Assert.Equal(0, messenger.PendingRequestCount);
        }
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private ControlledTimer? _timer;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new ControlledTimer(callback, state);

        public void Fire() => _timer!.Fire();

        private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _completed;

            public void Fire()
            {
                if (Interlocked.Exchange(ref _completed, 1) == 0)
                {
                    callback(state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) =>
                throw new NotSupportedException("This test uses one-shot timers.");

            public void Dispose() => Interlocked.Exchange(ref _completed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }
    }

    [Fact]
    public async Task ThrowingCancellationCallbackCannotPreventHandleCleanup()
    {
        var pair = Pair();
        await using var managed = pair.Managed;
        await using var native = pair.Native;
        using var server = native.GetMethodChannel("cleanup");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = server.SetMethodCallHandler(
            async (_, cancellation) =>
            {
                using var callback = cancellation.Register(() =>
                    throw new InvalidOperationException("Adapter cancellation failed")
                );
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                return null;
            }
        );
        using var client = managed.GetMethodChannel("cleanup");
        var pending = client.InvokeAsync("wait");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    private sealed class DelayedTransport : IChannelTransport
    {
        private readonly Dictionary<Guid, TaskCompletionSource<TransportResponse>> _requests = [];
        public TransportRequest LastRequest;

        public void SetMessageHandler(
            Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? handler
        ) { }

        public ValueTask<TransportResponse> SendAsync(
            TransportRequest request,
            CancellationToken cancellationToken = default
        )
        {
            LastRequest = request;
            var completion = new TaskCompletionSource<TransportResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _requests.Add(request.Id, completion);
            return new(completion.Task);
        }

        public void Complete(Guid id, byte[] payload) =>
            _requests[id].TrySetResult(new(TransportStatus.Success, payload));

        public ValueTask DisposeAsync()
        {
            foreach (var completion in _requests.Values)
            {
                completion.TrySetResult(new(TransportStatus.Disposed, default));
            }

            return default;
        }
    }
}

public sealed record FloatingDto(double Value);

public sealed record PaymentResult(string Status, long Id);

[JsonSerializable(typeof(PaymentResult))]
[JsonSerializable(typeof(FloatingDto))]
[JsonSerializable(typeof(List<double>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TestJsonContext : JsonSerializerContext;
