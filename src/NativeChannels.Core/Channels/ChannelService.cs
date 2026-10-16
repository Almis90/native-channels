namespace NativeChannels;

public sealed class ChannelService : IChannelService, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly HashSet<IAsyncDisposable> _handles = [];
    private readonly IBinaryMessenger _messenger;
    private readonly IMethodCodec _codec;
    private readonly IMessageCodec _messageCodec;
    private readonly ChannelSerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly ChannelLimits _limits;
    private bool _disposed;
    private Task? _disposeTask;

    public ChannelService(
        IBinaryMessenger messenger,
        ChannelSerializer? serializer = null,
        TimeSpan? timeout = null,
        ChannelLimits? limits = null
    )
    {
        _messenger = messenger;
        _limits = limits ?? new();
        _serializer = serializer ?? new();
        _messageCodec = new JsonMessageCodec(_serializer, limits);
        _codec = new JsonMethodCodec((JsonMessageCodec)_messageCodec);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        ChannelLimits.ValidateTimeout(_timeout);
    }

    public MethodChannel GetMethodChannel(string name, MethodChannelOptions? options = null)
    {
        ValidateName(name);
        var timeout = options?.Timeout ?? _timeout;
        ChannelLimits.ValidateTimeout(timeout);
        var codec = options?.Codec ?? _codec;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Acquire(name, "method", codec);

            var handle = new MethodChannel(
                _messenger,
                name,
                codec,
                _serializer,
                timeout,
                channel => Release(channel, channel.Name),
                options?.HandlerDispatcher
            );
            _handles.Add(handle);
            return handle;
        }
    }

    public BasicMessageChannel GetBasicMessageChannel(string name, BasicMessageChannelOptions? options = null)
    {
        ValidateName(name);
        var timeout = options?.Timeout ?? _timeout;
        ChannelLimits.ValidateTimeout(timeout);
        var codec = options?.Codec ?? _messageCodec;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Acquire(name, "basic", codec);
            var handle = new BasicMessageChannel(
                _messenger,
                name,
                codec,
                _serializer,
                timeout,
                channel => Release(channel, channel.Name),
                options?.HandlerDispatcher
            );
            _handles.Add(handle);
            return handle;
        }
    }

    public EventChannel GetEventChannel(string name, EventChannelOptions? options = null)
    {
        ValidateName(name);
        options ??= new();
        if (options.BufferCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(options.BufferCapacity));
        if (!Enum.IsDefined(options.OverflowPolicy))
            throw new ArgumentOutOfRangeException(nameof(options.OverflowPolicy));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Acquire(name, "event", _codec);
            var handle = new EventChannel(
                _messenger,
                name,
                _codec,
                _serializer,
                options,
                channel => Release(channel, channel.Name)
            );
            _handles.Add(handle);
            return handle;
        }
    }

    private void ValidateName(string name)
    {
        _limits.ValidateName(name);
        if (name.StartsWith(EventChannel.SubscriptionPrefix, StringComparison.Ordinal))
            throw new ArgumentException("This prefix is reserved for event subscriptions.", nameof(name));
    }

    private void Acquire(string name, string kind, object codec)
    {
        if (_endpoints.TryGetValue(name, out var endpoint))
        {
            if (endpoint.Kind != kind || !ReferenceEquals(endpoint.Codec, codec))
                throw new InvalidOperationException($"Conflicting channel kind or codec for '{name}'.");
            endpoint.Handles++;
        }
        else
            _endpoints.Add(name, new(kind, codec));
    }

    private void Release(IAsyncDisposable channel, string name)
    {
        lock (_gate)
        {
            if (!_handles.Remove(channel))
            {
                return;
            }

            if (--_endpoints[name].Handles == 0)
            {
                _endpoints.Remove(name);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            return new(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        IAsyncDisposable[] handles;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            handles = [.. _handles];
        }
        foreach (var handle in handles)
        {
            ((IDisposable)handle).Dispose();
        }

        await Task.WhenAll(handles.Select(handle => handle.DisposeAsync().AsTask())).ConfigureAwait(false);
        await _messenger.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => Disposal.Observe(DisposeAsync());

    private sealed class Endpoint(string kind, object codec)
    {
        public string Kind { get; } = kind;
        public object Codec { get; } = codec;
        public int Handles = 1;
    }
}
