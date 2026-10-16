namespace NativeChannels;

public class ChannelException(
    string code,
    string message,
    object? details = null,
    string? platform = null,
    Exception? inner = null
) : Exception(message, inner)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
    public string? Platform { get; } = platform;
}

public sealed class ChannelNotFoundException(string channel)
    : ChannelException("channel_not_found", $"No handler registered for '{channel}'.");

public sealed class MethodNotImplementedException(string? method = null)
    : ChannelException("not_implemented", $"Method '{method}' is not implemented.");

public sealed class ChannelSerializationException(string message, Exception? inner = null)
    : ChannelException("serialization_error", message, inner: inner);

public sealed class ChannelTransportException(string message, Exception? inner = null)
    : ChannelException("transport_error", message, inner: inner);

public sealed class EventBufferOverflowException(string channel, int capacity)
    : ChannelException("event_overflow", $"Event channel '{channel}' exceeded its buffer capacity of {capacity}.");
