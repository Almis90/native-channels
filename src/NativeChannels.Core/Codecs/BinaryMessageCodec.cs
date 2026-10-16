namespace NativeChannels;

/// <summary>Copies bytes using a one-byte null/present marker, preserving null versus empty.</summary>
public sealed class BinaryMessageCodec : IMessageCodec
{
    public static BinaryMessageCodec Instance { get; } = new();

    public ReadOnlyMemory<byte> Encode(object? value)
    {
        if (value is null)
            return new byte[] { 0 };
        if (value is not byte[] bytes)
            throw new ChannelSerializationException("BinaryMessageCodec requires byte[] or null.");
        var result = new byte[checked(bytes.Length + 1)];
        result[0] = 1;
        bytes.CopyTo(result, 1);
        return result;
    }

    public object? Decode(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length == 1 && payload.Span[0] == 0)
            return null;
        if (payload.IsEmpty || payload.Span[0] != 1)
            throw new ChannelSerializationException("Invalid binary message marker.");
        return payload[1..].ToArray();
    }
}
