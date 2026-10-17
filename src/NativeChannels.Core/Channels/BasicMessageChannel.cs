namespace NativeChannels;

/// <summary>A named, bidirectional value request/reply channel without method envelopes.</summary>
public sealed class BasicMessageChannel : IDisposable, IAsyncDisposable
{
    private readonly IBinaryMessenger _messenger;
    private readonly IMessageCodec _codec;
    private readonly ChannelSerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly Action<BasicMessageChannel> _release;
    private readonly ChannelHandlerDispatcher? _dispatcher;
    private readonly ChannelDiagnostics? _diagnostics;
    private readonly CancellationTokenSource _closed = new();
    private readonly object _gate = new();
    private IDisposable? _registration;
    private bool _disposed;

    internal BasicMessageChannel(
        IBinaryMessenger messenger,
        string name,
        IMessageCodec codec,
        ChannelSerializer serializer,
        TimeSpan timeout,
        Action<BasicMessageChannel> release,
        ChannelHandlerDispatcher? dispatcher,
        ChannelDiagnostics? diagnostics
    )
    {
        _messenger = messenger;
        Name = name;
        _codec = codec;
        _serializer = serializer;
        _timeout = timeout;
        _release = release;
        _dispatcher = dispatcher;
        _diagnostics = diagnostics;
    }

    public string Name { get; }

    public Task<object?> SendAsync(
        object? message,
        CancellationToken cancellationToken = default,
        ChannelInvocationOptions? options = null
    ) => SendCoreAsync<object>(message, cancellationToken, options);

    public Task<T?> SendAsync<T>(
        object? message,
        CancellationToken cancellationToken = default,
        ChannelInvocationOptions? options = null
    ) => SendCoreAsync<T>(message, cancellationToken, options);

    private async Task<T?> SendCoreAsync<T>(
        object? message,
        CancellationToken cancellationToken,
        ChannelInvocationOptions? options
    )
    {
        var timeout = options?.Timeout ?? _timeout;
        using var diagnostic = _diagnostics?.Start(
            ChannelDiagnosticKind.Message,
            ChannelDiagnosticDirection.Outgoing,
            Name,
            timeout: timeout
        );
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);
            ChannelLimits.ValidateTimeout(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
            try
            {
                var reply = await _messenger
                    .SendAsync(Name, _codec.Encode(message), timeout, linked.Token)
                    .ConfigureAwait(false);
                return _serializer.ConvertResult<T>(_codec.Decode(reply));
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(BasicMessageChannel));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        catch (Exception error)
        {
            diagnostic?.Fail(error);
            throw;
        }
    }

    public IDisposable SetMessageHandler(Func<object?, CancellationToken, ValueTask<object?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _registration = _messenger.RegisterHandler(
                Name,
                async (payload, cancellation) =>
                {
                    using var diagnostic = _diagnostics?.Start(
                        ChannelDiagnosticKind.Message,
                        ChannelDiagnosticDirection.Incoming,
                        Name
                    );
                    using var cancelled = diagnostic?.ObserveCancellation(cancellation) ?? default;
                    try
                    {
                        var message = _codec.Decode(payload);
                        var result = _dispatcher is null
                            ? await handler(message, cancellation).ConfigureAwait(false)
                            : await _dispatcher(() => handler(message, cancellation), cancellation)
                                .ConfigureAwait(false);
                        var response = _codec.Encode(result);
                        diagnostic?.Dispose();
                        return response;
                    }
                    catch (Exception error)
                    {
                        diagnostic?.Fail(error);
                        throw;
                    }
                }
            );
        }
    }

    public void RemoveMessageHandler()
    {
        lock (_gate)
        {
            _registration?.Dispose();
            _registration = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _registration?.Dispose();
            _registration = null;
        }
        Disposal.Cancel(_closed);
        _release(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
