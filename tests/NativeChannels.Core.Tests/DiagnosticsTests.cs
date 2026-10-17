using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace NativeChannels.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public async Task DisabledByDefaultAndDoesNotChangeResults()
    {
        var transports = InMemoryTransport.CreatePair();
        await using var client = new ChannelService(new BinaryMessenger(transports.Managed));
        await using var server = new ChannelService(new BinaryMessenger(transports.Native));
        Assert.Null(client.Diagnostics);
        using var handler = server.GetMethodChannel("test");
        handler.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(42));
        using var channel = client.GetMethodChannel("test");
        Assert.Equal(42, await channel.InvokeAsync<int>("read"));
        Assert.Throws<ArgumentNullException>(() => new ChannelDiagnostics(null!));
    }

    [Fact]
    public async Task ReportsBothDirectionsAndEffectiveTimeoutWithoutPayloads()
    {
        await using var pair = new Pair();
        using var server = pair.Server.GetMethodChannel("test");
        server.SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));
        using var client = pair.Client.GetMethodChannel("test", new() { Timeout = TimeSpan.FromSeconds(12) });
        Assert.Equal("private argument", await client.InvokeAsync<string>("echo", "private argument"));

        var outgoing = AssertOperation(pair.ClientEvents, ChannelDiagnosticOutcome.Success);
        Assert.Equal(ChannelDiagnosticKind.Method, outgoing.Kind);
        Assert.Equal(ChannelDiagnosticDirection.Outgoing, outgoing.Direction);
        Assert.Equal("test", outgoing.Channel);
        Assert.Equal("echo", outgoing.Method);
        Assert.Equal(TimeSpan.FromSeconds(12), outgoing.Timeout);
        var incoming = AssertOperation(pair.ServerEvents, ChannelDiagnosticOutcome.Success);
        Assert.Equal(ChannelDiagnosticDirection.Incoming, incoming.Direction);
        Assert.Equal("echo", incoming.Method);
        Assert.Null(incoming.Timeout);
        Assert.NotEqual(outgoing.OperationId, incoming.OperationId);
        Assert.DoesNotContain(
            "private argument",
            JsonSerializer.Serialize(pair.ClientEvents.Concat(pair.ServerEvents))
        );
    }

    [Theory]
    [InlineData("missing", ChannelDiagnosticOutcome.ChannelNotFound, "channel_not_found")]
    [InlineData("unknown", ChannelDiagnosticOutcome.NotImplemented, "not_implemented")]
    [InlineData("platform", ChannelDiagnosticOutcome.ChannelError, "declined")]
    [InlineData("unexpected", ChannelDiagnosticOutcome.ChannelError, "managed_error")]
    [InlineData("conversion", ChannelDiagnosticOutcome.SerializationError, "serialization_error")]
    [InlineData("encoding", ChannelDiagnosticOutcome.SerializationError, "serialization_error")]
    public async Task ReportsMethodFailuresWithoutExceptionMessagesOrDetails(
        string scenario,
        ChannelDiagnosticOutcome expected,
        string code
    )
    {
        await using var pair = new Pair();
        using var server = pair.Server.GetMethodChannel("test");
        server.SetMethodCallHandler(
            (call, _) =>
                call.Method switch
                {
                    "unknown" => throw new MethodNotImplementedException("private method error"),
                    "platform" => throw new ChannelException(
                        "declined",
                        "private error",
                        new Dictionary<string, object?> { ["secret"] = "private details" },
                        "ios"
                    ),
                    "unexpected" => throw new InvalidOperationException("private exception"),
                    _ => ValueTask.FromResult<object?>(null),
                }
        );
        using var client = pair.Client.GetMethodChannel(scenario == "missing" ? "missing" : "test");
        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.InvokeAsync<int>(scenario, scenario == "encoding" ? double.NaN : null)
        );
        var completed = AssertOperation(pair.ClientEvents, expected);
        Assert.Equal(code, completed.ErrorCode);
        if (scenario == "platform")
            Assert.Equal("ios", completed.Platform);
        if (scenario == "unexpected")
            AssertOperation(pair.ServerEvents, ChannelDiagnosticOutcome.Failed);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(pair.ClientEvents.Concat(pair.ServerEvents)));
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BasicMessagesReportTypedConversionAndSuccessfulNull(bool invalidConversion)
    {
        await using var pair = new Pair();
        using var server = pair.Server.GetBasicMessageChannel("message");
        server.SetMessageHandler((_, _) => ValueTask.FromResult<object?>(null));
        using var client = pair.Client.GetBasicMessageChannel("message");
        if (invalidConversion)
            await Assert.ThrowsAsync<ChannelSerializationException>(() => client.SendAsync<int>("private"));
        else
            Assert.Null(await client.SendAsync<string>("private"));
        var completed = AssertOperation(
            pair.ClientEvents,
            invalidConversion ? ChannelDiagnosticOutcome.SerializationError : ChannelDiagnosticOutcome.Success
        );
        Assert.Equal(ChannelDiagnosticKind.Message, completed.Kind);
        Assert.Null(completed.Method);
        AssertOperation(pair.ServerEvents, ChannelDiagnosticOutcome.Success);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(pair.ClientEvents.Concat(pair.ServerEvents)));
    }

    [Fact]
    public async Task BinaryMessagesAreNotIncludedInDiagnostics()
    {
        await using var pair = new Pair();
        var options = new BasicMessageChannelOptions { Codec = BinaryMessageCodec.Instance };
        using var server = pair.Server.GetBasicMessageChannel("binary", options);
        server.SetMessageHandler((value, _) => ValueTask.FromResult(value));
        using var client = pair.Client.GetBasicMessageChannel("binary", options);
        var bytes = "private audio payload"u8.ToArray();
        Assert.Equal(bytes, await client.SendAsync<byte[]>(bytes));
        AssertOperation(pair.ClientEvents, ChannelDiagnosticOutcome.Success);
        var json = JsonSerializer.Serialize(pair.ClientEvents.Concat(pair.ServerEvents));
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain(Convert.ToBase64String(bytes), json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutsAndLateRepliesHaveOnlyOneCompletion(bool message)
    {
        var clock = new ManualTimeProvider();
        await using var pair = new Pair(clock);
        var entered = Signal();
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var serverMethod = pair.Server.GetMethodChannel("method");
        using var serverMessage = pair.Server.GetBasicMessageChannel("message");
        ValueTask<object?> Wait()
        {
            entered.TrySetResult();
            return new(release.Task);
        }
        serverMethod.SetMethodCallHandler((_, _) => Wait());
        serverMessage.SetMessageHandler((_, _) => Wait());
        using var clientMethod = pair.Client.GetMethodChannel("method");
        using var clientMessage = pair.Client.GetBasicMessageChannel("message");
        var pending = message ? clientMessage.SendAsync(null) : clientMethod.InvokeAsync<object>("wait");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Fire();
        await Assert.ThrowsAsync<TimeoutException>(() => pending);
        AssertOperation(pair.ClientEvents, ChannelDiagnosticOutcome.TimedOut);
        AssertOperation(pair.ServerEvents, ChannelDiagnosticOutcome.Cancelled);
        release.SetResult("late private result");
        // A following request confirms the dispatcher still works after the timed-out operation.
        await clientMethod.InvokeAsync("after", options: new() { Timeout = Timeout.InfiniteTimeSpan });
        Assert.Equal(2, pair.ClientEvents.Count(e => e.Stage == ChannelDiagnosticStage.Completed));
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationAndDisposalReportActualCallerOutcome(bool message, bool dispose)
    {
        await using var pair = new Pair();
        var entered = Signal();
        async ValueTask<object?> Wait(CancellationToken token)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }
        using var serverMethod = pair.Server.GetMethodChannel("method");
        using var serverMessage = pair.Server.GetBasicMessageChannel("message");
        serverMethod.SetMethodCallHandler((_, token) => Wait(token));
        serverMessage.SetMessageHandler((_, token) => Wait(token));
        using var clientMethod = pair.Client.GetMethodChannel("method");
        using var clientMessage = pair.Client.GetBasicMessageChannel("message");
        using var cancellation = new CancellationTokenSource();
        var options = new ChannelInvocationOptions { Timeout = Timeout.InfiniteTimeSpan };
        var pending = message
            ? clientMessage.SendAsync(null, cancellation.Token, options)
            : clientMethod.InvokeAsync<object>("wait", cancellationToken: cancellation.Token, options: options);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose)
        {
            if (message)
                clientMessage.Dispose();
            else
                clientMethod.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        }
        else
        {
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        var completed = AssertOperation(
            pair.ClientEvents,
            dispose ? ChannelDiagnosticOutcome.Disposed : ChannelDiagnosticOutcome.Cancelled
        );
        Assert.Equal(Timeout.InfiniteTimeSpan, completed.Timeout);
        AssertOperation(pair.ServerEvents, ChannelDiagnosticOutcome.Cancelled);
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task TransportFailuresAndMalformedIncomingDataAreVisible()
    {
        var failure = new ChannelTransportException("private bridge message");
        var events = new ConcurrentQueue<ChannelDiagnosticEvent>();
        await using var service = new ChannelService(new BinaryMessenger(new FailingTransport(failure)))
        {
            Diagnostics = new(events.Enqueue),
        };
        using var channel = service.GetMethodChannel("test");
        Assert.Same(failure, await Assert.ThrowsAsync<ChannelTransportException>(() => channel.InvokeAsync("call")));
        AssertOperation(events, ChannelDiagnosticOutcome.TransportError);

        await using var pair = new Pair();
        using var handler = pair.Server.GetMethodChannel("test");
        handler.SetMethodCallHandler((_, _) => throw new Exception("Must not execute"));
        await pair.Messenger.SendAsync("test", "invalid private data"u8.ToArray(), TimeSpan.FromSeconds(5));
        var malformed = AssertOperation(pair.ServerEvents, ChannelDiagnosticOutcome.SerializationError);
        Assert.Null(malformed.Method);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(events.Concat(pair.ServerEvents)));
    }

    [Fact]
    public async Task ThrowingObserversCannotChangeSuccessErrorsOrCleanup()
    {
        await using var pair = new Pair(observer: _ => throw new InvalidOperationException("Logger failed"));
        using var server = pair.Server.GetMethodChannel("test");
        server.SetMethodCallHandler(
            (call, _) =>
                call.Method == "error"
                    ? throw new ChannelException("declined", "Expected error")
                    : ValueTask.FromResult<object?>(42)
        );
        using var client = pair.Client.GetMethodChannel("test");
        Assert.Equal(42, await client.InvokeAsync<int>("read"));
        Assert.Equal("declined", (await Assert.ThrowsAsync<ChannelException>(() => client.InvokeAsync("error"))).Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.InvokeAsync("cancelled", cancellationToken: cancellation.Token)
        );
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.InvokeAsync("disposed"));
        Assert.Equal(0, pair.Messenger.PendingRequestCount);
    }

    [Fact]
    public async Task ReentrantConcurrentCallsKeepIndependentPairedRecords()
    {
        await using var pair = new Pair();
        using var server = pair.Server.GetMethodChannel("reentrant");
        using var client = pair.Client.GetMethodChannel("reentrant");
        client.SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));
        server.SetMethodCallHandler(
            async (call, token) => await server.InvokeAsync<int>("callback", call.Arguments, token)
        );
        Assert.Equal(
            Enumerable.Range(0, 100),
            await Task.WhenAll(Enumerable.Range(0, 100).Select(i => client.InvokeAsync<int>("echo", i)))
        );
        foreach (var events in new[] { pair.ClientEvents, pair.ServerEvents })
        {
            Assert.Equal(200, events.GroupBy(e => e.OperationId).Count());
            foreach (var operation in events.GroupBy(e => e.OperationId))
                AssertOperation(operation, ChannelDiagnosticOutcome.Success);
        }
    }

    [Fact]
    public async Task EventProtocolCallsAreObservableWithoutEventValues()
    {
        await using var pair = new Pair();
        using var server = pair.Server.GetEventChannel("events");
        server.SetStreamHandler((_, token) => Values(token));
        using var client = pair.Client.GetEventChannel("events");
        var values = new List<string?>();
        await foreach (var value in client.ListenAsync<string>())
            values.Add(value);
        Assert.Equal(new[] { "private event" }, values);
        var completed = pair.ClientEvents.Where(e => e.Stage == ChannelDiagnosticStage.Completed).ToArray();
        Assert.Contains(
            completed,
            e =>
                e.Method == "listen"
                && e.Direction == ChannelDiagnosticDirection.Outgoing
                && e.Timeout == Timeout.InfiniteTimeSpan
        );
        Assert.Contains(completed, e => e.Method == "event" && e.Direction == ChannelDiagnosticDirection.Incoming);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(pair.ClientEvents));

        static async IAsyncEnumerable<object?> Values([EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            yield return "private event";
        }
    }

    [Fact]
    public async Task NativeViewLeasePropagatesDiagnosticsToInstanceChannel()
    {
        var transports = InMemoryTransport.CreatePair();
        var events = new ConcurrentQueue<ChannelDiagnosticEvent>();
        await using var remote = new ChannelService(new BinaryMessenger(transports.Native));
        await using var lease = new ViewChannelLease(
            transports.Managed,
            new(),
            TimeSpan.FromSeconds(30),
            new(),
            diagnostics: new(events.Enqueue)
        );
        using var server = remote.GetMethodChannel(lease.Channel.Name);
        server.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(42));
        Assert.Equal(42, await lease.Channel.InvokeAsync<int>("pageCount"));
        Assert.Equal(lease.Channel.Name, AssertOperation(events, ChannelDiagnosticOutcome.Success).Channel);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ChannelDiagnosticEvent AssertOperation(
        IEnumerable<ChannelDiagnosticEvent> events,
        ChannelDiagnosticOutcome outcome
    )
    {
        var pair = events.ToArray();
        Assert.Equal(2, pair.Length);
        Assert.Equal(ChannelDiagnosticStage.Started, pair[0].Stage);
        Assert.Null(pair[0].Outcome);
        Assert.Null(pair[0].ErrorCode);
        Assert.Equal(TimeSpan.Zero, pair[0].Elapsed);
        Assert.Equal(ChannelDiagnosticStage.Completed, pair[1].Stage);
        Assert.Equal(pair[0].OperationId, pair[1].OperationId);
        Assert.NotEqual(Guid.Empty, pair[0].OperationId);
        Assert.Equal(outcome, pair[1].Outcome);
        Assert.True(pair[1].Elapsed >= TimeSpan.Zero);
        return pair[1];
    }

    private sealed class Pair : IAsyncDisposable
    {
        public readonly ConcurrentQueue<ChannelDiagnosticEvent> ClientEvents = new();
        public readonly ConcurrentQueue<ChannelDiagnosticEvent> ServerEvents = new();
        public BinaryMessenger Messenger { get; }
        public ChannelService Client { get; }
        public ChannelService Server { get; }

        public Pair(TimeProvider? clock = null, Action<ChannelDiagnosticEvent>? observer = null)
        {
            var transports = InMemoryTransport.CreatePair();
            Messenger = new(transports.Managed, timeProvider: clock);
            Client = new(Messenger) { Diagnostics = new(observer ?? ClientEvents.Enqueue) };
            Server = new(new BinaryMessenger(transports.Native))
            {
                Diagnostics = new(observer ?? ServerEvents.Enqueue),
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }

    private sealed class FailingTransport(Exception failure) : IChannelTransport
    {
        public void SetMessageHandler(
            Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? handler
        ) { }

        public ValueTask<TransportResponse> SendAsync(
            TransportRequest request,
            CancellationToken cancellationToken = default
        ) => throw failure;

        public ValueTask DisposeAsync() => default;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new ManualTimer();
        }

        public void Fire() => _callback!(_state);

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => default;
        }
    }
}
