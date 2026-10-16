using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NativeChannels;

/// <summary>Each enumeration owns a distinct subscription, buffer, and remote listen request.</summary>
public sealed class EventChannel : IDisposable, IAsyncDisposable
{
    internal const string SubscriptionPrefix = "__maui.events/";
    private readonly IBinaryMessenger _messenger;
    private readonly IMethodCodec _codec;
    private readonly ChannelSerializer _serializer;
    private readonly EventChannelOptions _options;
    private readonly Action<EventChannel> _release;
    private readonly CancellationTokenSource _closed = new();
    private readonly object _gate = new();
    private readonly HashSet<Subscription> _subscriptions = [];
    private readonly MethodChannel _control;
    private bool _disposed;
    private Task? _disposeTask;

    internal EventChannel(
        IBinaryMessenger messenger,
        string name,
        IMethodCodec codec,
        ChannelSerializer serializer,
        EventChannelOptions options,
        Action<EventChannel> release
    )
    {
        _messenger = messenger;
        Name = name;
        _codec = codec;
        _serializer = serializer;
        _options = options;
        _release = release;
        _control = CreateMethodChannel(name);
    }

    public string Name { get; }

    /// <summary>Streams have no invocation timeout. Cancel or dispose the enumeration to unsubscribe.</summary>
    public async IAsyncEnumerable<T?> ListenAsync<T>(
        object? arguments = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Subscription subscription;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            subscription = new Subscription(this, arguments, cancellationToken);
            _subscriptions.Add(subscription);
            subscription.Start();
        }
        try
        {
            while (await subscription.WaitToReadAsync().ConfigureAwait(false))
            {
                while (subscription.Reader.TryRead(out var value))
                {
                    subscription.ThrowIfCancelled();
                    yield return _serializer.ConvertResult<T>(value);
                }
            }
        }
        finally
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Serves independent streams to the remote endpoint. The token owns this handler.</summary>
    public IDisposable SetStreamHandler(Func<object?, CancellationToken, IAsyncEnumerable<object?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _control.SetMethodCallHandler(
                async (call, cancellation) =>
                {
                    if (call.Method != "listen")
                        throw new MethodNotImplementedException(call.Method);
                    var target = call.GetArgument<string>("events");
                    if (
                        target is null
                        || !target.StartsWith(SubscriptionPrefix, StringComparison.Ordinal)
                        || !Guid.TryParseExact(target[SubscriptionPrefix.Length..], "N", out _)
                    )
                        throw new ChannelException("invalid_arguments", "Invalid event subscription endpoint.");
                    using var events = CreateMethodChannel(target);
                    await foreach (
                        var value in handler(call.GetArgument<object>("arguments"), cancellation)
                            .WithCancellation(cancellation)
                            .ConfigureAwait(false)
                    )
                    {
                        await events.InvokeAsync("event", value, cancellation).ConfigureAwait(false);
                    }
                    return null;
                }
            );
        }
    }

    public void RemoveStreamHandler() => _control.RemoveMethodCallHandler();

    private MethodChannel CreateMethodChannel(string name) =>
        new(_messenger, name, _codec, _serializer, Timeout.InfiniteTimeSpan, _ => { }, null);

    public void Dispose() => Disposal.Observe(DisposeAsync());

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Subscription[] subscriptions;
        lock (_gate)
        {
            _disposed = true;
            subscriptions = [.. _subscriptions];
        }
        Disposal.Cancel(_closed);
        _control.Dispose();
        await Task.WhenAll(subscriptions.Select(s => s.DisposeAsync().AsTask())).ConfigureAwait(false);
        _release(this);
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly EventChannel _owner;
        private readonly object? _arguments;
        private readonly CancellationTokenSource _lifetime;
        private readonly CancellationToken _caller;
        private readonly Channel<object?> _buffer;
        private readonly MethodChannel _events;
        private Task _listening = Task.CompletedTask;
        private readonly object _lifetimeGate = new();
        private bool _lifetimeDisposed;
        public ChannelReader<object?> Reader => _buffer.Reader;

        public Subscription(EventChannel owner, object? arguments, CancellationToken caller)
        {
            _owner = owner;
            _arguments = arguments;
            _caller = caller;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, owner._closed.Token);
            _buffer = Channel.CreateBounded<object?>(
                new BoundedChannelOptions(owner._options.BufferCapacity)
                {
                    SingleReader = true,
                    AllowSynchronousContinuations = false,
                    FullMode = owner._options.OverflowPolicy switch
                    {
                        EventOverflowPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
                        EventOverflowPolicy.DropNewest => BoundedChannelFullMode.DropWrite,
                        _ => BoundedChannelFullMode.Wait,
                    },
                }
            );
            _events = owner.CreateMethodChannel(SubscriptionPrefix + Guid.NewGuid().ToString("N"));
            _events.SetMethodCallHandler(
                (call, _) =>
                {
                    if (call.Method != "event")
                        throw new MethodNotImplementedException(call.Method);
                    if (!_buffer.Writer.TryWrite(call.Arguments))
                    {
                        var error = new EventBufferOverflowException(owner.Name, owner._options.BufferCapacity);
                        _buffer.Writer.TryComplete(error);
                        Stop();
                        throw error;
                    }
                    return ValueTask.FromResult<object?>(null);
                }
            );
        }

        public void Start() => _listening = ListenAsync();

        private async Task ListenAsync()
        {
            try
            {
                await _owner
                    ._control.InvokeAsync(
                        "listen",
                        new Dictionary<string, object?> { ["events"] = _events.Name, ["arguments"] = _arguments },
                        _lifetime.Token
                    )
                    .ConfigureAwait(false);
                _buffer.Writer.TryComplete();
            }
            catch (Exception error)
            {
                _buffer.Writer.TryComplete(error);
            }
            finally
            {
                _events.Dispose();
                lock (_owner._gate)
                    _owner._subscriptions.Remove(this);
            }
        }

        public void ThrowIfCancelled()
        {
            ObjectDisposedException.ThrowIf(_owner._closed.IsCancellationRequested, _owner);
            _caller.ThrowIfCancellationRequested();
        }

        public async ValueTask<bool> WaitToReadAsync()
        {
            ThrowIfCancelled();
            // Lifetime cancellation also carries overflow; read the buffer's terminal error instead.
            using var callerLifetime = CancellationTokenSource.CreateLinkedTokenSource(_caller, _owner._closed.Token);
            try
            {
                return await Reader.WaitToReadAsync(callerLifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ThrowIfCancelled();
                throw;
            }
        }

        private void Stop()
        {
            lock (_lifetimeGate)
            {
                if (!_lifetimeDisposed)
                    Disposal.Cancel(_lifetime);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            await _listening.ConfigureAwait(false);
            lock (_lifetimeGate)
            {
                if (!_lifetimeDisposed)
                {
                    _lifetimeDisposed = true;
                    _lifetime.Dispose();
                }
            }
        }
    }
}
