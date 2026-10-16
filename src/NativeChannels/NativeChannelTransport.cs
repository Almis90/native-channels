using System.Collections.Concurrent;

namespace NativeChannels;

public sealed partial class NativeChannelTransport : IChannelTransport
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? _handler;
    private Task? _shutdown;
    private bool _disposed;

    public NativeChannelTransport(IEnumerable<NativeModuleRegistration>? modules = null) => Initialize(modules ?? []);

    public void SetMessageHandler(Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? handler)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _handler = handler;
        }
    }

    public async ValueTask<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new Pending();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_pending.TryAdd(request.Id, pending))
            {
                throw new ChannelTransportException("Duplicate request ID.");
            }

            try
            {
                pending.Peer = SendNative(request, response => pending.Completion.TrySetResult(response));
            }
            catch
            {
                _pending.TryRemove(request.Id, out _);
                throw;
            }
        }
        try
        {
            using var registration = cancellationToken.Register(() =>
            {
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        CancelNative(request.Id);
                    }
                }
            });
            return await pending.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
            pending.Peer?.Dispose();
        }
    }

    private async ValueTask<TransportResponse> ReceiveAsync(
        TransportRequest request,
        CancellationToken cancellationToken
    )
    {
        Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? handler;
        lock (_gate)
        {
            if (_disposed)
            {
                return new(TransportStatus.Disposed, default);
            }

            handler = _handler;
        }
        if (handler is null)
        {
            return new(TransportStatus.ChannelNotFound, default);
        }

        try
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(TransportStatus.Cancelled, default);
        }
        catch (Exception)
        {
            return new(TransportStatus.HandlerError, default);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_shutdown is not null)
            {
                return new(_shutdown);
            }

            _disposed = true;
            _handler = null;
            _shutdown = ShutdownNativeAsync();
            return new(_shutdown);
        }
    }

    private sealed class Pending
    {
        public IDisposable? Peer;
        public readonly TaskCompletionSource<TransportResponse> Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
    }

    private partial void Initialize(IEnumerable<NativeModuleRegistration> modules);

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed);

    private partial void CancelNative(Guid id);

    private partial Task ShutdownNativeAsync();
}
