using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace NativeChannels;

/// <summary>A map whose keys may be any supported value, including null or a collection.</summary>
public sealed record StandardMessageMap(IReadOnlyList<KeyValuePair<object?, object?>> Entries);

/// <summary>Compact, little-endian tagged values and typed numeric arrays. Select the same codec on both peers.</summary>
public sealed class StandardMessageCodec : IMessageCodec
{
    public static StandardMessageCodec Instance { get; } = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly ChannelLimits _limits;

    public StandardMessageCodec(ChannelLimits? limits = null)
    {
        _limits = limits ?? new();
        _limits.Validate();
    }

    public ReadOnlyMemory<byte> Encode(object? value)
    {
        try
        {
            var writer = new Writer(_limits);
            writer.Value(value, 0);
            return writer.Bytes;
        }
        catch (EncoderFallbackException error)
        {
            throw new ChannelSerializationException("Invalid UTF-16 string.", error);
        }
    }

    public object? Decode(ReadOnlyMemory<byte> payload)
    {
        _limits.ValidatePayload(payload);
        try
        {
            var reader = new Reader(payload.Span, _limits.MaximumDepth);
            var result = reader.Value(0);
            if (reader.Remaining != 0)
                throw Invalid();
            return result;
        }
        catch (DecoderFallbackException error)
        {
            throw new ChannelSerializationException("Invalid UTF-8 string.", error);
        }
    }

    private static ChannelSerializationException Invalid() => new("Malformed or unsupported standard message value.");

    private sealed class Writer(ChannelLimits limits)
    {
        private readonly MemoryStream _buffer = new();
        public byte[] Bytes => _buffer.ToArray();

        private void Ensure(int count)
        {
            if (count < 0 || count > limits.MaximumPayloadBytes - _buffer.Length)
                throw new ChannelSerializationException("Standard message exceeds the payload limit.");
        }

        private void Byte(byte value)
        {
            Ensure(1);
            _buffer.WriteByte(value);
        }

        private void Raw(ReadOnlySpan<byte> bytes)
        {
            Ensure(bytes.Length);
            _buffer.Write(bytes);
        }

        private void Number(ulong value, int width)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            Raw(bytes[..width]);
        }

        private void Size(int size)
        {
            if (size < 0)
                throw Invalid();
            if (size < 254)
                Byte((byte)size);
            else if (size <= ushort.MaxValue)
            {
                Byte(254);
                Number((uint)size, 2);
            }
            else
            {
                Byte(255);
                Number((uint)size, 4);
            }
        }

        private void Align(int width)
        {
            while (_buffer.Length % width != 0)
                Byte(0);
        }

        private void Array<T>(byte tag, T[] values, int width, Func<T, ulong> bits)
        {
            Byte(tag);
            Size(values.Length);
            Align(width);
            if (values.Length > (limits.MaximumPayloadBytes - _buffer.Length) / width)
                throw Invalid();
            foreach (var item in values)
                Number(bits(item), width);
        }

        public void Value(object? value, int depth)
        {
            if (depth > limits.MaximumDepth)
                throw new ChannelSerializationException("Standard message is too deep or cyclic.");
            switch (value)
            {
                case null:
                    Byte(0);
                    break;
                case bool boolean:
                    Byte(boolean ? (byte)1 : (byte)2);
                    break;
                case byte or sbyte or short or ushort or int:
                    Byte(3);
                    Number(unchecked((uint)Convert.ToInt32(value)), 4);
                    break;
                case uint unsigned:
                    Byte(4);
                    Number(unsigned, 8);
                    break;
                case long integer:
                    Byte(4);
                    Number(unchecked((ulong)integer), 8);
                    break;
                case float single:
                    Value((double)single, depth);
                    break;
                case double number:
                    Byte(6);
                    Align(8);
                    Number(BitConverter.DoubleToUInt64Bits(number), 8);
                    break;
                case string text:
                    Byte(7);
                    var count = Utf8.GetByteCount(text);
                    Size(count);
                    Ensure(count);
                    Raw(Utf8.GetBytes(text));
                    break;
                case byte[] bytes:
                    Byte(8);
                    Size(bytes.Length);
                    Raw(bytes);
                    break;
                case int[] items:
                    Array(9, items, 4, x => unchecked((uint)x));
                    break;
                case long[] items:
                    Array(10, items, 8, x => unchecked((ulong)x));
                    break;
                case double[] items:
                    Array(11, items, 8, BitConverter.DoubleToUInt64Bits);
                    break;
                case float[] items:
                    Array(14, items, 4, x => BitConverter.SingleToUInt32Bits(x));
                    break;
                case StandardMessageMap map:
                    Byte(13);
                    Size(map.Entries.Count);
                    foreach (var entry in map.Entries)
                    {
                        Value(entry.Key, depth + 1);
                        Value(entry.Value, depth + 1);
                    }
                    break;
                case IDictionary map:
                    Byte(13);
                    Size(map.Count);
                    foreach (DictionaryEntry entry in map)
                    {
                        Value(entry.Key, depth + 1);
                        Value(entry.Value, depth + 1);
                    }
                    break;
                case IList list:
                    Byte(12);
                    Size(list.Count);
                    foreach (var item in list)
                        Value(item, depth + 1);
                    break;
                default:
                    throw new ChannelSerializationException(
                        $"Unsupported standard message type: {value.GetType().Name}."
                    );
            }
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes, int maximumDepth)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private readonly int _maximumDepth = maximumDepth;
        private int _position;
        public int Remaining => _bytes.Length - _position;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining)
                throw Invalid();
            var result = _bytes.Slice(_position, count);
            _position += count;
            return result;
        }

        private byte Byte() => Take(1)[0];

        private ulong Number(int width)
        {
            var source = Take(width);
            ulong result = 0;
            for (var i = 0; i < width; i++)
                result |= (ulong)source[i] << (8 * i);
            return result;
        }

        private int Size()
        {
            var first = Byte();
            var size =
                first == 254 ? Number(2)
                : first == 255 ? Number(4)
                : first;
            if (size > int.MaxValue)
                throw Invalid();
            return (int)size;
        }

        private void Align(int width)
        {
            var padding = Take((width - _position % width) % width);
            foreach (var item in padding)
                if (item != 0)
                    throw Invalid();
        }

        private T[] Array<T>(int width, Func<ulong, T> convert)
        {
            var count = Size();
            Align(width);
            if (count > Remaining / width)
                throw Invalid();
            var result = new T[count];
            for (var i = 0; i < count; i++)
                result[i] = convert(Number(width));
            return result;
        }

        public object? Value(int depth)
        {
            if (depth > _maximumDepth)
                throw Invalid();
            switch (Byte())
            {
                case 0:
                    return null;
                case 1:
                    return true;
                case 2:
                    return false;
                case 3:
                    return unchecked((int)Number(4));
                case 4:
                    return unchecked((long)Number(8));
                case 6:
                    Align(8);
                    return BitConverter.UInt64BitsToDouble(Number(8));
                case 7:
                    return Utf8.GetString(Take(Size()));
                case 8:
                    return Take(Size()).ToArray();
                case 9:
                    return Array(4, x => unchecked((int)x));
                case 10:
                    return Array(8, x => unchecked((long)x));
                case 11:
                    return Array(8, BitConverter.UInt64BitsToDouble);
                case 14:
                    return Array(4, x => BitConverter.UInt32BitsToSingle((uint)x));
                case 12:
                    var count = Size();
                    if (count > Remaining)
                        throw Invalid();
                    var list = new List<object?>();
                    for (var i = 0; i < count; i++)
                        list.Add(Value(depth + 1));
                    return list;
                case 13:
                    var pairs = Size();
                    if (pairs > Remaining / 2)
                        throw Invalid();
                    var entries = new List<KeyValuePair<object?, object?>>();
                    var named = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var allNamed = true;
                    for (var i = 0; i < pairs; i++)
                    {
                        var key = Value(depth + 1);
                        var value = Value(depth + 1);
                        entries.Add(new(key, value));
                        if (key is not string name || !named.TryAdd(name, value))
                            allNamed = false;
                    }
                    return allNamed ? named : new StandardMessageMap(entries);
                default:
                    throw Invalid();
            }
        }
    }
}
