using System.Text;

namespace NativeChannels;

/// <summary>Strict UTF-8 with the same null/present marker as BinaryMessageCodec.</summary>
public sealed class StringMessageCodec : IMessageCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static StringMessageCodec Instance { get; } = new();

    public ReadOnlyMemory<byte> Encode(object? value)
    {
        if (value is null)
            return BinaryMessageCodec.Instance.Encode(null);
        if (value is not string text)
            throw new ChannelSerializationException("StringMessageCodec requires string or null.");
        try
        {
            return BinaryMessageCodec.Instance.Encode(Utf8.GetBytes(text));
        }
        catch (EncoderFallbackException error)
        {
            throw new ChannelSerializationException("Invalid Unicode string.", error);
        }
    }

    public object? Decode(ReadOnlyMemory<byte> payload)
    {
        var bytes = (byte[]?)BinaryMessageCodec.Instance.Decode(payload);
        try
        {
            return bytes is null ? null : Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException error)
        {
            throw new ChannelSerializationException("Invalid UTF-8 message.", error);
        }
    }
}
