using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NativeChannels;

public sealed class BinaryMessenger : IBinaryMessenger, IDisposable
{
    private readonly IChannelTransport _transport;
    private readonly ChannelLimits _limits;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _closed = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ReadOnlyMemory<byte>>> _pending = new();
    private readonly ConcurrentDictionary<string, Registration> _handlers = new(StringComparer.Ordinal);
    private readonly Channel<Incoming> _incoming = Channel.CreateUnbounded<Incoming>(
        new() { SingleReader = true, AllowSynchronousContinuations = false }
    );
    private readonly Task _dispatch;
    private int _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public BinaryMessenger(IChannelTransport transport, ChannelLimits? limits = null, TimeProvider? timeProvider = null)
    {
        _transport = transport;
        _limits = limits ?? new();
        _limits.Validate();
        _time = timeProvider ?? TimeProvider.System;
        _transport.SetMessageHandler(ReceiveAsync);
        _dispatch = Task.Run(DispatchAsync);
    }

    public int PendingRequestCount => _pending.Count;

    public IDisposable RegisterHandler(
        string channel,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> handler
    )
    {
        ThrowIfDisposed();
        _limits.ValidateName(channel);
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new Registration(this, channel, handler);
        if (!_handlers.TryAdd(channel, registration))
        {
            throw new InvalidOperationException($"A handler is already registered for '{channel}'.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            registration.Dispose();
            ThrowIfDisposed();
        }
        return registration;
    }

    public async ValueTask<ReadOnlyMemory<byte>> SendAsync(
        string channel,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        _limits.ValidateName(channel);
        _limits.ValidatePayload(payload);
        ChannelLimits.ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        using var timer = new CancellationTokenSource(timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timer.Token,
            _closed.Token
        );
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending[id] = completion;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            _ = TransmitAsync(new(id, channel, payload.ToArray()), linked.Token);
            return await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(BinaryMessenger));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested)
        {
            throw new TimeoutException($"Channel '{channel}' exceeded {timeout}.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task TransmitAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _limits.ValidatePayload(response.Payload);
            if (!_pending.TryGetValue(request.Id, out var completion))
            {
                return;
            }

            switch (response.Status)
            {
                case TransportStatus.Success:
                    completion.TrySetResult(response.Payload.ToArray());
                    break;
                case TransportStatus.ChannelNotFound:
                    completion.TrySetException(new ChannelNotFoundException(request.Channel));
                    break;
                case TransportStatus.Disposed:
                    completion.TrySetException(new ObjectDisposedException("Native session"));
                    break;
                case TransportStatus.Cancelled:
                    completion.TrySetCanceled(cancellationToken);
                    break;
                default:
                    completion.TrySetException(new ChannelTransportException("Remote binary handler failed."));
                    break;
            }
        }
        catch (Exception exception)
        {
            if (_pending.TryGetValue(request.Id, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private ValueTask<TransportResponse> ReceiveAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return ValueTask.FromResult(new TransportResponse(TransportStatus.Disposed, default));
        }

        try
        {
            _limits.ValidateName(request.Channel);
            _limits.ValidatePayload(request.Payload);
        }
        catch (ArgumentException)
        {
            return ValueTask.FromResult(new TransportResponse(TransportStatus.HandlerError, default));
        }
        catch (ChannelException)
        {
            return ValueTask.FromResult(new TransportResponse(TransportStatus.HandlerError, default));
        }
        var completion = new TaskCompletionSource<TransportResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        if (
            !_incoming.Writer.TryWrite(
                new(request with { Payload = request.Payload.ToArray() }, cancellationToken, completion)
            )
        )
        {
            completion.TrySetResult(new(TransportStatus.Disposed, default));
        }

        return new(completion.Task);
    }

    private async Task DispatchAsync()
    {
        await foreach (var incoming in _incoming.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            _ = HandleAsync(incoming); // Invoke in FIFO order, but release the queue at the first asynchronous suspension.
        }
    }

    private async Task HandleAsync(Incoming incoming)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                incoming.Completion.TrySetResult(new(TransportStatus.Disposed, default));
                return;
            }
            if (!_handlers.TryGetValue(incoming.Request.Channel, out var registration))
            {
                incoming.Completion.TrySetResult(new(TransportStatus.ChannelNotFound, default));
                return;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                incoming.Cancellation,
                _closed.Token,
                registration.Removed.Token
            );
            linked.Token.ThrowIfCancellationRequested();
            var result = await registration
                .Handler(incoming.Request.Payload, linked.Token)
                .AsTask()
                .WaitAsync(linked.Token)
                .ConfigureAwait(false);
            _limits.ValidatePayload(result);
            incoming.Completion.TrySetResult(new(TransportStatus.Success, result.ToArray()));
        }
        catch (OperationCanceledException)
        {
            incoming.Completion.TrySetResult(new(TransportStatus.Cancelled, default));
        }
        catch (Exception)
        {
            incoming.Completion.TrySetResult(new(TransportStatus.HandlerError, default));
        }
    }

    public void Dispose() => Disposal.Observe(DisposeAsync());

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            return new(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Disposal.Cancel(_closed);
        _incoming.Writer.TryComplete();
        foreach (var registration in _handlers.Values)
        {
            registration.Dispose();
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new ObjectDisposedException(nameof(BinaryMessenger)));
        }

        _pending.Clear();
        await _transport.DisposeAsync().ConfigureAwait(false);
        await _dispatch.ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record Incoming(
        TransportRequest Request,
        CancellationToken Cancellation,
        TaskCompletionSource<TransportResponse> Completion
    );

    private sealed class Registration(
        BinaryMessenger owner,
        string channel,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> handler
    ) : IDisposable
    {
        public readonly CancellationTokenSource Removed = new();
        public readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> Handler =
            handler;

        public void Dispose()
        {
            if (owner._handlers.TryRemove(new KeyValuePair<string, Registration>(channel, this)))
            {
                Disposal.Cancel(Removed);
            }
        }
    }
}
