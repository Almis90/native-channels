using System.Runtime.CompilerServices;
using Xunit;

namespace NativeChannels.Linux.Tests;

public sealed class LinuxTransportTests
{
    [Fact]
    public async Task MethodsCanReenterTheManagedEndpointAndPreserveErrors()
    {
        var module = new Module(channels =>
        {
            var methods = channels.GetMethodChannel("test");
            methods.SetMethodCallHandler(
                async (call, cancellation) =>
                    call.Method switch
                    {
                        "callback" => await methods.InvokeAsync<long>("managed", cancellationToken: cancellation),
                        "fail" => throw new ChannelException("linux_error", "failure", 42, "linux"),
                        _ => throw new MethodNotImplementedException(call.Method),
                    }
            );
        });
        await using var service = CreateService(() => module);
        using var channel = service.GetMethodChannel("test");
        channel.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(long.MaxValue));
        Assert.Equal(long.MaxValue, await channel.InvokeAsync<long>("callback"));
        var error = await Assert.ThrowsAsync<ChannelException>(() => channel.InvokeAsync("fail"));
        Assert.Equal("linux_error", error.Code);
        Assert.Equal(42, error.Details);
        await Assert.ThrowsAsync<MethodNotImplementedException>(() => channel.InvokeAsync("missing"));
        using var missing = service.GetMethodChannel("missing");
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => missing.InvokeAsync("call"));
    }

    [Fact]
    public async Task MessagesAndIndependentEventSubscriptionsRoundTrip()
    {
        var module = new Module(channels =>
        {
            var messages = channels.GetBasicMessageChannel("bytes", new() { Codec = BinaryMessageCodec.Instance });
            messages.SetMessageHandler(async (value, token) => await messages.SendAsync(value, token));
            channels.GetEventChannel("events").SetStreamHandler(Events);
        });
        await using var service = CreateService(() => module);
        using var messages = service.GetBasicMessageChannel("bytes", new() { Codec = BinaryMessageCodec.Instance });
        messages.SetMessageHandler((value, _) => ValueTask.FromResult(value));
        Assert.Equal(new byte[] { 0, 127, 255 }, await messages.SendAsync<byte[]>(new byte[] { 0, 127, 255 }));
        using var events = service.GetEventChannel("events");
        var first = ReadEvents(events, 10);
        var second = ReadEvents(events, 20);
        Assert.Equal(new object?[] { null, 10 }, await first);
        Assert.Equal(new object?[] { null, 20 }, await second);
    }

    [Fact]
    public async Task CancellationReachesAdapterAndOtherHandlesSurvive()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module(channels =>
            channels
                .GetMethodChannel("test")
                .SetMethodCallHandler(
                    async (call, token) =>
                    {
                        if (call.Method == "echo")
                        {
                            return call.Arguments;
                        }
                        using var registration = token.Register(() => cancelled.TrySetResult());
                        started.TrySetResult();
                        await Task.Delay(Timeout.Infinite, token);
                        return null;
                    }
                )
        );
        await using var service = CreateService(() => module);
        using var first = service.GetMethodChannel("test");
        using var second = service.GetMethodChannel("test");
        using var cancellation = new CancellationTokenSource();
        var pending = first.InvokeAsync("wait", cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        Assert.Equal(42, await second.InvokeAsync<int>("echo", 42));
    }

    [Fact]
    public async Task DisposalCompletesUncooperativeRequestsAndDisposesModulesOnce()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module(channels =>
            channels
                .GetMethodChannel("wait")
                .SetMethodCallHandler(
                    (_, _) =>
                    {
                        started.TrySetResult();
                        return new(release.Task);
                    }
                )
        );
        var service = CreateService(() => module);
        try
        {
            using var channel = service.GetMethodChannel("wait");
            var pending = channel.InvokeAsync("wait");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
            await service.DisposeAsync();
            Assert.Equal(1, module.Disposals);
        }
        finally
        {
            release.TrySetResult(null);
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task InitializationRollsBackEveryCreatedModule()
    {
        var first = new Module(_ => { });
        var second = new Module(_ => throw new InvalidOperationException("registration failed"));
        var error = Assert.Throws<ChannelTransportException>(() =>
            new NativeChannelTransport([Registration(() => first), Registration(() => second)])
        );
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.NotNull(first.Channels);
        Assert.Throws<ObjectDisposedException>(() => first.Channels.GetMethodChannel("closed"));
        await first.Channels.DisposeAsync();
    }

    [Fact]
    public void MissingLinuxAdapterFailsWithActionableError()
    {
        var error = Assert.Throws<ChannelTransportException>(() =>
            new NativeChannelTransport([new("android.Module", "AppleModule")])
        );
        Assert.IsType<PlatformNotSupportedException>(error.InnerException);
        Assert.Contains("LinuxFactory", error.InnerException.Message);
    }

    [Fact]
    public async Task ThrowingModuleCleanupDoesNotSkipOtherModules()
    {
        var first = new Module(_ => { });
        var second = new Module(_ => { }, throwOnDispose: true);
        var transport = new NativeChannelTransport([Registration(() => first), Registration(() => second)]);
        await Assert.ThrowsAsync<AggregateException>(() => transport.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<AggregateException>(() => transport.DisposeAsync().AsTask());
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Fact]
    public async Task SessionsCreateIndependentAdaptersAndCorrelateConcurrentReplies()
    {
        var created = 0;
        Module Factory()
        {
            Interlocked.Increment(ref created);
            return new(channels =>
                channels
                    .GetMethodChannel("delay")
                    .SetMethodCallHandler(
                        async (call, token) =>
                        {
                            var delay = call.GetArguments<int>();
                            await Task.Delay(delay, token);
                            return delay;
                        }
                    )
            );
        }
        await using var first = CreateService(Factory);
        await using var second = CreateService(Factory);
        using var firstChannel = first.GetMethodChannel("delay");
        using var secondChannel = second.GetMethodChannel("delay");
        var expected = Enumerable.Range(1, 16).Reverse().ToArray();
        Assert.Equal(
            expected,
            await Task.WhenAll(expected.Select(value => firstChannel.InvokeAsync<int>("delay", value)))
        );
        await first.DisposeAsync();
        Assert.Equal(1, await secondChannel.InvokeAsync<int>("delay", 1));
        Assert.Equal(2, created);
    }

    private static NativeModuleRegistration Registration(Func<ILinuxChannelModule> factory) =>
        new("", "") { LinuxFactory = factory };

    private static ChannelService CreateService(Func<ILinuxChannelModule> factory) =>
        new(new BinaryMessenger(new NativeChannelTransport([Registration(factory)])));

    private static async IAsyncEnumerable<object?> Events(
        object? value,
        [EnumeratorCancellation] CancellationToken cancellation
    )
    {
        yield return null;
        await Task.Yield();
        cancellation.ThrowIfCancellationRequested();
        yield return value;
    }

    private static async Task<List<object?>> ReadEvents(EventChannel events, int value)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = new List<object?>();
        await foreach (var item in events.ListenAsync<object>(value, deadline.Token))
        {
            result.Add(item);
        }
        return result;
    }

    private sealed class Module(Action<IChannelService> register, bool throwOnDispose = false) : ILinuxChannelModule
    {
        public int Disposals { get; private set; }
        public IChannelService? Channels { get; private set; }

        public void Register(IChannelService channels)
        {
            Channels = channels;
            register(channels);
        }

        public void Dispose()
        {
            Disposals++;
            if (throwOnDispose)
            {
                throw new InvalidOperationException("cleanup failed");
            }
        }
    }
}
