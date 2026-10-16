namespace NativeChannels;

public interface IMessageCodec
{
    ReadOnlyMemory<byte> Encode(object? value);
    object? Decode(ReadOnlyMemory<byte> payload);
}

public interface IMethodCodec
{
    ReadOnlyMemory<byte> EncodeCall(MethodCall call);
    MethodCall DecodeCall(ReadOnlyMemory<byte> payload);
    ReadOnlyMemory<byte> EncodeSuccess(object? value);
    ReadOnlyMemory<byte> EncodeError(ChannelException error);
    ReadOnlyMemory<byte> EncodeNotImplemented();
    object? DecodeResult(ReadOnlyMemory<byte> payload);
}
