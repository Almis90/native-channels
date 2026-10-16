namespace NativeChannels;

public interface IBinaryMessenger : IAsyncDisposable
{
    ValueTask<ReadOnlyMemory<byte>> SendAsync(
        string channel,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    );
    IDisposable RegisterHandler(
        string channel,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> handler
    );
}
