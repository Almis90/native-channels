namespace NativeChannels;

public sealed class MethodChannel : IDisposable, IAsyncDisposable
{
    private readonly IBinaryMessenger _messenger;
    private readonly IMethodCodec _codec;
    private readonly ChannelSerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly Action<MethodChannel> _release;
    private readonly CancellationTokenSource _closed = new();
    private readonly object _gate = new();
    private IDisposable? _registration;
    private bool _disposed;
    private readonly ChannelHandlerDispatcher? _dispatcher;
    private readonly ChannelDiagnostics? _diagnostics;

    internal MethodChannel(
        IBinaryMessenger messenger,
        string name,
        IMethodCodec codec,
        ChannelSerializer serializer,
        TimeSpan timeout,
        Action<MethodChannel> release,
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

    public async Task InvokeAsync(
        string method,
        object? arguments = null,
        CancellationToken cancellationToken = default,
        ChannelInvocationOptions? options = null
    ) => _ = await InvokeCoreAsync<object>(method, arguments, cancellationToken, options).ConfigureAwait(false);

    public async Task<T?> InvokeAsync<T>(
        string method,
        object? arguments = null,
        CancellationToken cancellationToken = default,
        ChannelInvocationOptions? options = null
    ) => await InvokeCoreAsync<T>(method, arguments, cancellationToken, options).ConfigureAwait(false);

    private async Task<T?> InvokeCoreAsync<T>(
        string method,
        object? arguments,
        CancellationToken cancellationToken,
        ChannelInvocationOptions? options
    )
    {
        var timeout = options?.Timeout ?? _timeout;
        using var diagnostic = _diagnostics?.Start(
            ChannelDiagnosticKind.Method,
            ChannelDiagnosticDirection.Outgoing,
            Name,
            method,
            timeout
        );
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            ChannelLimits.ValidateTimeout(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
            try
            {
                var payload = _codec.EncodeCall(new(method, arguments));
                var response = await _messenger.SendAsync(Name, payload, timeout, linked.Token).ConfigureAwait(false);
                return _serializer.ConvertResult<T>(_codec.DecodeResult(response));
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(MethodChannel));
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

    public IDisposable SetMethodCallHandler(Func<MethodCall, CancellationToken, ValueTask<object?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var registration = _messenger.RegisterHandler(
                Name,
                (payload, cancellationToken) => HandleIncomingCallAsync(payload, cancellationToken, handler)
            );
            _registration = registration;
            return registration;
        }
    }

    private async ValueTask<ReadOnlyMemory<byte>> HandleIncomingCallAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken,
        Func<MethodCall, CancellationToken, ValueTask<object?>> handler
    )
    {
        using var diagnostic = _diagnostics?.Start(
            ChannelDiagnosticKind.Method,
            ChannelDiagnosticDirection.Incoming,
            Name
        );
        using var cancellation = diagnostic?.ObserveCancellation(cancellationToken) ?? default;
        try
        {
            var call = _codec.DecodeCall(payload);
            if (diagnostic is not null)
                diagnostic.Method = call.Method;
            object? result;
            if (_dispatcher is null)
            {
                result = await handler(call, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await _dispatcher(() => handler(call, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }

            var response = _codec.EncodeSuccess(result);
            diagnostic?.Dispose();
            return response;
        }
        catch (MethodNotImplementedException error)
        {
            diagnostic?.Fail(error);
            return _codec.EncodeNotImplemented();
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            diagnostic?.Fail(error);
            throw;
        }
        catch (ChannelException error)
        {
            diagnostic?.Fail(error);
            return _codec.EncodeError(error);
        }
        catch (Exception error)
        {
            diagnostic?.Fail(error);
            return _codec.EncodeError(new("managed_error", "Managed handler failed.", platform: "managed"));
        }
    }

    public void RemoveMethodCallHandler()
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
            {
                return;
            }

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
