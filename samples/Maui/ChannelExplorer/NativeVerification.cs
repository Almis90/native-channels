using Microsoft.Maui.ApplicationModel;

namespace ChannelExplorer;

internal sealed class NativeVerification(IChannelService channels)
{
    public async Task<string> RunAsync()
    {
        using var channel = channels.GetMethodChannel(
            "sample",
            new() { HandlerDispatcher = MauiChannelDispatchers.MainThread }
        );
        using var incoming = channel.SetMethodCallHandler(HandleNativeCallAsync);

        var device = await VerifyValuesAndCallbacksAsync(channel);
        await VerifyNativeUiDispatchAsync();
        await VerifyErrorsAsync(channel);
        await VerifyConcurrentCallsAsync(channel);
        await VerifyTimeoutsAndCancellationAsync(channel);
        await VerifyHandleDisposalAsync(channel);
        await VerifyMessagesAsync();
        await VerifyEventsAsync(channel);
        await VerifySessionDisposalAsync(channel);

        return $"{device.Platform}: typed calls, DTOs, bytes, null, errors, callbacks, UI dispatch, "
            + "16 concurrent calls, cancellation, timeouts, handle and session disposal, "
            + "bidirectional JSON/string/binary/standard messages, null events, stream errors and independent subscription cleanup";
    }

    private static async ValueTask<object?> HandleNativeCallAsync(MethodCall call, CancellationToken cancellation)
    {
        Check(MainThread.IsMainThread, "Managed UI dispatcher");
        if (call.Method != "getToken")
        {
            throw new MethodNotImplementedException(call.Method);
        }

        await Task.Delay(5, cancellation);
        return "managed-token";
    }

    private static async Task<DeviceInfo> VerifyValuesAndCallbacksAsync(MethodChannel channel)
    {
        var sum = await channel.InvokeAsync<int>("add", new AddArguments(10, 20));
        Check(sum == 30, "Typed arguments");

        var device = await channel.InvokeAsync<DeviceInfo>("device");
        Check(device is { Id: long.MaxValue }, "Typed DTO and exact Int64");

        Check(await channel.InvokeAsync<string>("null") is null, "Successful null");
        await channel.InvokeAsync("null");

        var callback = await channel.InvokeAsync<string>("callback");
        Check(callback == "native:managed-token", "Reentrant native callback");

        var bytes = new byte[] { 0, 1, 127, 255 };
        var returnedBytes = await channel.InvokeAsync<byte[]>("echo", bytes);
        Check(returnedBytes is not null && returnedBytes.SequenceEqual(bytes), "Binary codec value");
        Check(await channel.InvokeAsync<long>("echo", long.MinValue) == long.MinValue, "Negative Int64 boundary");

        return device!;
    }

    private async Task VerifyNativeUiDispatchAsync()
    {
        using var channel = channels.GetMethodChannel("sample.ui");
        Check(await channel.InvokeAsync<bool>("isMainThread"), "Native UI dispatcher");
    }

    private static async Task VerifyErrorsAsync(MethodChannel channel)
    {
        await ExpectExceptionAsync<MethodNotImplementedException>(
            () => channel.InvokeAsync("unknown"),
            "Unknown method succeeded"
        );

        var error = await ExpectExceptionAsync<ChannelException>(
            () => channel.InvokeAsync("fail"),
            "Native failure succeeded"
        );
        Check(error.Code == "sample_error", "Structured native error code");
    }

    private static async Task VerifyConcurrentCallsAsync(MethodChannel channel)
    {
        // Later requests finish first; each result must still reach its original caller.
        var expected = Enumerable.Range(1, 16).Select(index => (17 - index) * 3).ToArray();
        var actual = await Task.WhenAll(expected.Select(delay => channel.InvokeAsync<int>("delay", delay)));
        Check(actual.SequenceEqual(expected), "Concurrent response correlation");
    }

    private static async Task VerifyTimeoutsAndCancellationAsync(MethodChannel channel)
    {
        await ExpectExceptionAsync<TimeoutException>(
            () => channel.InvokeAsync("delay", 500, options: new() { Timeout = TimeSpan.FromMilliseconds(20) }),
            "Timeout did not fire"
        );

        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
        {
            await ExpectExceptionAsync<OperationCanceledException>(
                () => channel.InvokeAsync("delay", 500, cancellation.Token),
                "Cancellation did not fire"
            );
        }

        var result = await channel.InvokeAsync<int>("delay", 30, options: new() { Timeout = Timeout.InfiniteTimeSpan });
        Check(result == 30, "Infinite timeout");
    }

    private async Task VerifyHandleDisposalAsync(MethodChannel survivingChannel)
    {
        using (var disposedChannel = channels.GetMethodChannel("sample"))
        {
            var pending = disposedChannel.InvokeAsync("delay", 500);
            disposedChannel.Dispose();
            await ExpectExceptionAsync<ObjectDisposedException>(
                () => pending,
                "Disposed handle completed successfully"
            );
        }

        var sum = await survivingChannel.InvokeAsync<int>("add", new AddArguments(1, 2));
        Check(sum == 3, "Other handles survive disposal");
    }

    private static async Task VerifySessionDisposalAsync(MethodChannel survivingChannel)
    {
        var modules = new NativeModuleRegistration[] { MauiProgram.CreateSampleModule() };
        var transport = new NativeChannelTransport(modules);
        await using (var session = new ChannelService(new BinaryMessenger(transport)))
        {
            using var channel = session.GetMethodChannel("sample");
            var pending = channel.InvokeAsync("delay", 500);
            await Task.Delay(10);
            await session.DisposeAsync();
            await ExpectExceptionAsync<ObjectDisposedException>(
                () => pending,
                "Disposed native session completed successfully"
            );
        }

        Check(await survivingChannel.InvokeAsync<int>("echo", 42) == 42, "Other native sessions survive disposal");
    }

    private async Task VerifyMessagesAsync()
    {
        await VerifyStandardMessagesAsync();
        foreach (
            var (name, codec, value) in new (string, IMessageCodec?, object?)[]
            {
                ("json", null, long.MaxValue),
                ("string", StringMessageCodec.Instance, "€😀"),
                ("binary", BinaryMessageCodec.Instance, new byte[] { 0, 127, 255 }),
            }
        )
        {
            using var messages = channels.GetBasicMessageChannel("sample.messages." + name, new() { Codec = codec });
            using var handler = messages.SetMessageHandler((message, _) => ValueTask.FromResult(message));
            var reply = await messages.SendAsync(value);
            Check(
                value is byte[] bytes
                    ? reply is byte[] returned && bytes.SequenceEqual(returned)
                    : Equals(value, reply),
                "Bidirectional " + name + " message"
            );
            Check(await messages.SendAsync(null) is null, name + " null message");
        }
    }

    private async Task VerifyStandardMessagesAsync()
    {
        var codec = StandardMessageCodec.Instance;
        using var messages = channels.GetBasicMessageChannel("sample.messages.standard", new() { Codec = codec });
        using var handler = messages.SetMessageHandler((value, _) => ValueTask.FromResult(value));
        object?[] values =
        [
            null,
            true,
            false,
            int.MinValue,
            long.MaxValue,
            1.25d,
            "€😀",
            new byte[] { 0, 255 },
            new int[] { int.MinValue, int.MaxValue },
            new long[] { long.MinValue, long.MaxValue },
            new float[] { -0f, 1.25f, float.PositiveInfinity },
            new double[] { -0d, double.NegativeInfinity, 1.25 },
            new List<object?> { null, 1, 1L, new int[] { 2, 3 } },
            new Dictionary<string, object?> { ["value"] = new float[] { 0.5f } },
            new StandardMessageMap([new(null, "null key"), new(1, "int"), new(1L, "long")]),
        ];
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var started = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine(
                $"NATIVECHANNELS_PROGRESS standard value {index + 1}/{values.Length}: {value?.GetType().Name ?? "null"}"
            );
            var reply = await messages.SendAsync(value);
            Check(
                codec.Encode(value).Span.SequenceEqual(codec.Encode(reply).Span),
                "Bidirectional standard value/type"
            );
            Console.WriteLine(
                $"NATIVECHANNELS_PROGRESS standard value {index + 1} passed in {started.ElapsedMilliseconds} ms"
            );
        }
        Check(
            (await messages.SendAsync<int[]>(new[] { 1, 2 }))!.SequenceEqual(new[] { 1, 2 }),
            "Typed standard result"
        );
    }

    private async Task VerifyEventsAsync(MethodChannel methods)
    {
        // This budget covers the entire event suite, including simulator scheduling delays.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var events = channels.GetEventChannel("sample.events");
        var received = new List<object?>();
        await foreach (var value in events.ListenAsync<object>("finite", deadline.Token))
            received.Add(value);
        Check(
            received.Count == 2 && received[0] is null && Equals(received[1], 42),
            "Null event and stream completion"
        );

        received.Clear();
        var error = await ExpectExceptionAsync<ChannelException>(
            async () =>
            {
                await foreach (var value in events.ListenAsync<int>("error", deadline.Token))
                    received.Add(value);
            },
            "Native stream failure succeeded"
        );
        Check(
            error.Code == "sample_stream_error" && Equals(error.Details, 42) && received.Count == 1,
            "Buffered event followed by structured native error"
        );

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await using var first = events.ListenAsync<int>("live", cancellation.Token).GetAsyncEnumerator();
        await using var second = events.ListenAsync<int>("live", deadline.Token).GetAsyncEnumerator();
        Check(
            await first.MoveNextAsync() && await second.MoveNextAsync() && first.Current != second.Current,
            "Each enumeration owns an independent native subscription"
        );
        Check(await methods.InvokeAsync<int>("activeStreams") == 2, "Two active native subscriptions");
        var pending = first.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await ExpectExceptionAsync<OperationCanceledException>(() => pending, "Stream cancellation did not fire");
        await WaitForSubscriptionsAsync(methods, 1, deadline.Token);
        await second.DisposeAsync();
        await WaitForSubscriptionsAsync(methods, 0, deadline.Token);
    }

    private static async Task WaitForSubscriptionsAsync(
        MethodChannel methods,
        int expected,
        CancellationToken cancellation
    )
    {
        while (await methods.InvokeAsync<int>("activeStreams", cancellationToken: cancellation) != expected)
            await Task.Delay(10, cancellation);
    }

    private static async Task<TException> ExpectExceptionAsync<TException>(Func<Task> action, string failure)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(failure);
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Scenario failed: " + scenario);
        }
    }
}
