using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;

namespace UnoFeasibility;

internal static class HostProbe
{
    public static async Task RunAsync(Window window, ContentPresenter surface)
    {
        var host = Application.Current.UseNativeChannels(services =>
        {
            services.AddSingleton<Peer>();
            services.AddSingleton<IChannelService>(provider => provider.GetRequiredService<Peer>().Managed);
        });
        Require(ReferenceEquals(host, Application.Current.GetNativeChannels()), "application registration");
        await using var first = host.CreateSession(window);
        var otherWindow = new Window();
        await using var second = host.CreateSession(otherWindow);
        Require(!ReferenceEquals(first.Channels, second.Channels), "independent windows");
        var dispatcher = first.Dispatcher;
        var value = await Task.Run(async () =>
            await dispatcher.DispatchAsync(
                async () =>
                {
                    Require(dispatcher.HasThreadAccess, "UI dispatch");
                    await Task.Delay(10);
                    Require(dispatcher.HasThreadAccess, "async UI continuation");
                    return 42;
                },
                default
            )
        );
        Require(Equals(value, 42), "async result");
        var nested = await dispatcher.DispatchAsync(
            async () => await dispatcher.DispatchAsync(() => ValueTask.FromResult<object?>(7), default),
            default
        );
        Require(Equals(nested, 7), "reentrant dispatch");
        using var cancellation = new CancellationTokenSource();
        var started = false;
        var cancelled = dispatcher
            .DispatchAsync(
                () =>
                {
                    started = true;
                    return ValueTask.FromResult<object?>(null);
                },
                cancellation.Token
            )
            .AsTask();
        cancellation.Cancel();
        try
        {
            await cancelled;
            throw new InvalidOperationException("Expected dispatcher cancellation");
        }
        catch (OperationCanceledException) { }
        Require(!started, "cancelled queued work");
        try
        {
            await dispatcher.DispatchAsync(() => throw new ArgumentException("probe"), default);
            throw new InvalidOperationException("Expected handler error");
        }
        catch (ArgumentException) { }

        if (!OperatingSystem.IsMacOS())
        {
            var adapters = new List<View>();
            await using var view = new UnoChannelView(id =>
            {
                var adapter = new View(id, first.Channels.GetMethodChannel("view/" + id));
                adapters.Add(adapter);
                return adapter;
            });
            surface.Content = view;
            surface.UpdateLayout();
            await Task.Delay(100);
            Require(view.Channel is not null, "view loaded");
            var old = view.Channel!;
            var identity = view.InstanceId;
            view.Reconnect();
            Require(view.InstanceId != identity, "view recreation");
            try
            {
                await old.InvokeAsync("stale");
                throw new InvalidOperationException("Expected invalidated channel");
            }
            catch (ObjectDisposedException) { }
            await view.DisconnectAsync();
            surface.Content = null;
            Require(
                adapters.Count == 2 && adapters.All(adapter => adapter.Destroyed && adapter.Disposed),
                "view cleanup"
            );
        }

        using var methods = first.Channels.GetMethodChannel("pending");
        var peer = first.Services.GetRequiredService<Peer>();
        using var events = first.Channels.GetEventChannel("live");
        await using var subscription = events.ListenAsync<int>().GetAsyncEnumerator();
        Require(await subscription.MoveNextAsync() && subscription.Current == 1, "active subscription");
        var pending = methods.InvokeAsync("wait");
        await peer.Started.Task;
        var closing = first.DisposeAsync().AsTask();
        Require(ReferenceEquals(closing, first.DisposeAsync().AsTask()), "shared disposal completion");
        await closing;
        await peer.StreamStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await pending;
            throw new InvalidOperationException("Expected closed call");
        }
        catch (ObjectDisposedException) { }
        Require(
            first.Stopping.IsCancellationRequested && !second.Stopping.IsCancellationRequested,
            "isolated shutdown"
        );
        using var surviving = second.Channels.GetMethodChannel("alive");
        await Task.Run(async () => await host.DisposeAsync());
        Require(second.Stopping.IsCancellationRequested, "host shutdown");
        await host.DisposeAsync();
        otherWindow.Close();
    }

    private static void Require(bool value, string operation)
    {
        if (!value)
            throw new InvalidOperationException("Uno host probe failed: " + operation);
    }

    private sealed class Peer : IAsyncDisposable
    {
        public ChannelService Managed { get; }
        private readonly ChannelService _native;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StreamStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Peer()
        {
            var pair = InMemoryTransport.CreatePair();
            Managed = new(new BinaryMessenger(pair.Managed));
            _native = new(new BinaryMessenger(pair.Native));
            _native.GetEventChannel("live").SetStreamHandler(Stream);
            _native
                .GetMethodChannel("pending")
                .SetMethodCallHandler(
                    async (_, token) =>
                    {
                        Started.TrySetResult();
                        await Task.Delay(Timeout.Infinite, token);
                        return null;
                    }
                );
        }

        private async IAsyncEnumerable<object?> Stream(
            object? arguments,
            [EnumeratorCancellation] CancellationToken token
        )
        {
            try
            {
                yield return 1;
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                StreamStopped.TrySetResult();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Managed.DisposeAsync();
            await _native.DisposeAsync();
        }
    }

    private sealed class View(string id, MethodChannel channel) : IUnoNativeView
    {
        // A managed element exercises lifecycle only; it is not native embedding evidence.
        public object NativeElement { get; } = new TextBlock { Text = "Host lifecycle probe" };
        public MethodChannel Channel => channel;
        public string InstanceId => id;
        public bool Destroyed { get; private set; }
        public bool Disposed { get; private set; }

        public void Destroy() => Destroyed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
