namespace NativeChannels;

public sealed record MethodChannelOptions
{
    public TimeSpan? Timeout { get; init; }
    public IMethodCodec? Codec { get; init; }
    public ChannelHandlerDispatcher? HandlerDispatcher { get; init; }
}

public delegate ValueTask<object?> ChannelHandlerDispatcher(
    Func<ValueTask<object?>> handler,
    CancellationToken cancellationToken
);

public sealed record ChannelInvocationOptions
{
    public TimeSpan? Timeout { get; init; }
}

public sealed record BasicMessageChannelOptions
{
    public TimeSpan? Timeout { get; init; }
    public IMessageCodec? Codec { get; init; }
    public ChannelHandlerDispatcher? HandlerDispatcher { get; init; }
}

public enum EventOverflowPolicy
{
    Fail,
    DropOldest,
    DropNewest,
}

public sealed record EventChannelOptions
{
    public int BufferCapacity { get; init; } = 64;
    public EventOverflowPolicy OverflowPolicy { get; init; } = EventOverflowPolicy.Fail;
}
