namespace NativeChannels;

/// <summary>A pair of isolated bidirectional endpoints for in-process adapters and platform-independent tests.</summary>
public sealed class InMemoryTransport : IChannelTransport
{
    private readonly CancellationTokenSource _closed = new();
    private readonly object _gate = new();
    private InMemoryTransport _peer = null!;
    private Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? _handler;
    private bool _disposed;

    private InMemoryTransport() { }

    public static (InMemoryTransport Managed, InMemoryTransport Native) CreatePair()
    {
        var first = new InMemoryTransport();
        var second = new InMemoryTransport();
        first._peer = second;
        second._peer = first;
        return (first, second);
    }

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
        Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? handler;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        lock (_peer._gate)
        {
            ObjectDisposedException.ThrowIf(_peer._disposed, _peer);
            handler = _peer._handler;
        }
        if (handler is null)
        {
            return new(TransportStatus.ChannelNotFound, default);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _closed.Token,
            _peer._closed.Token
        );
        var response = await handler(request with { Payload = request.Payload.ToArray() }, linked.Token)
            .AsTask()
            .WaitAsync(linked.Token)
            .ConfigureAwait(false);
        return response with { Payload = response.Payload.ToArray() };
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return default;
            }

            _disposed = true;
            _handler = null;
        }
        Disposal.Cancel(_closed);
        return default;
    }
}
