namespace NativeChannels;

/// <summary>One connection owns an isolated bridge and channel; reconnect never reuses either.</summary>
internal sealed class ViewChannelLease : IAsyncDisposable
{
    private readonly ChannelService _service;
    private Task? _disposal;
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public MethodChannel Channel { get; }

    public ViewChannelLease(
        IChannelTransport transport,
        ChannelSerializer serializer,
        TimeSpan timeout,
        ChannelLimits limits,
        ChannelHandlerDispatcher? dispatcher = null,
        ChannelDiagnostics? diagnostics = null
    )
    {
        _service = new(new BinaryMessenger(transport, limits), serializer, timeout, limits)
        {
            Diagnostics = diagnostics,
        };
        Channel = _service.GetMethodChannel(
            "nativechannels.view." + InstanceId,
            new() { HandlerDispatcher = dispatcher }
        );
    }

    public ValueTask DisposeAsync()
    {
        Channel.Dispose();
        return new(_disposal ??= _service.DisposeAsync().AsTask());
    }
}
