namespace NativeChannels;

public readonly record struct TransportRequest(Guid Id, string Channel, ReadOnlyMemory<byte> Payload);

public enum TransportStatus
{
    Success,
    ChannelNotFound,
    HandlerError,
    Disposed,
    Cancelled,
}

public readonly record struct TransportResponse(TransportStatus Status, ReadOnlyMemory<byte> Payload);

/// <summary>A session-scoped, asynchronous request/reply boundary. Implementations copy borrowed native buffers.</summary>
public interface IChannelTransport : IAsyncDisposable
{
    void SetMessageHandler(Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? handler);
    ValueTask<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default);
}
