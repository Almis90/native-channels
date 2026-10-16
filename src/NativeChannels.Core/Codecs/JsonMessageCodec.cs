using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace NativeChannels;

public sealed class JsonMessageCodec : IMessageCodec
{
    internal ChannelLimits Limits { get; }
    internal ChannelSerializer Serializer { get; }

    public JsonMessageCodec(ChannelSerializer? serializer = null, ChannelLimits? limits = null)
    {
        Serializer = serializer ?? new();
        Limits = limits ?? new();
        Limits.Validate();
    }

    public ReadOnlyMemory<byte> Encode(object? value) =>
        Write(writer => WriteValue(writer, Serializer.Normalize(value)));

    public object? Decode(ReadOnlyMemory<byte> payload)
    {
        using var document = Parse(payload);
        return ReadValue(document.RootElement);
    }

    internal ReadOnlyMemory<byte> Write(Action<Utf8JsonWriter> write)
    {
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new() { MaxDepth = Limits.MaximumDepth * 3 + 16 }))
            {
                write(writer);
            }

            // The returned memory owns this per-call buffer; nothing reuses or mutates it after publication.
            var bytes = buffer.WrittenMemory;
            Limits.ValidatePayload(bytes);
            return bytes;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
        {
            throw new ChannelSerializationException("Cannot encode channel payload.", exception);
        }
    }

    internal JsonDocument Parse(ReadOnlyMemory<byte> payload)
    {
        Limits.ValidatePayload(payload);
        try
        {
            return JsonDocument.Parse(payload, new() { MaxDepth = Limits.MaximumDepth * 3 + 16 });
        }
        catch (JsonException exception)
        {
            throw new ChannelSerializationException("Invalid channel JSON.", exception);
        }
    }

    internal void WriteValue(Utf8JsonWriter writer, object? value, int depth = 0)
    {
        if (depth > Limits.MaximumDepth)
        {
            throw new ChannelSerializationException("Value nesting limit exceeded.");
        }

        writer.WriteStartArray();
        switch (value)
        {
            case null:
                writer.WriteStringValue("n");
                break;
            case bool boolean:
                writer.WriteStringValue("b");
                writer.WriteBooleanValue(boolean);
                break;
            case string text:
                writer.WriteStringValue("s");
                writer.WriteStringValue(text);
                break;
            case int integer:
                writer.WriteStringValue("i32");
                writer.WriteNumberValue(integer);
                break;
            case long integer:
                writer.WriteStringValue("i64");
                writer.WriteStringValue(integer.ToString(CultureInfo.InvariantCulture));
                break;
            case double number when double.IsFinite(number):
                writer.WriteStringValue("f64");
                writer.WriteNumberValue(number);
                break;
            case byte[] bytes:
                writer.WriteStringValue("bytes");
                writer.WriteBase64StringValue(bytes);
                break;
            case IDictionary<string, object?> map:
                writer.WriteStringValue("map");
                writer.WriteStartObject();
                foreach (var item in map)
                {
                    writer.WritePropertyName(item.Key);
                    WriteValue(writer, item.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case IEnumerable<object?> list:
                writer.WriteStringValue("list");
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(writer, item, depth + 1);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new ChannelSerializationException("Unsupported channel value or non-finite number.");
        }
        writer.WriteEndArray();
    }

    internal object? ReadValue(JsonElement value, int depth = 0)
    {
        try
        {
            if (depth > Limits.MaximumDepth || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            {
                throw new ChannelSerializationException("Invalid tagged value.");
            }

            var tag = value[0].GetString();
            if (value.GetArrayLength() != (tag == "n" ? 1 : 2))
            {
                throw new ChannelSerializationException("Invalid tagged value length.");
            }

            return tag switch
            {
                "n" => null,
                "b" => value[1].GetBoolean(),
                "s" => value[1].GetString() ?? throw new ChannelSerializationException("String cannot be null."),
                "i32" => value[1].GetInt32(),
                "i64" => long.Parse(value[1].GetString()!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                "f64" when double.IsFinite(value[1].GetDouble()) => value[1].GetDouble(),
                "bytes" => value[1].GetBytesFromBase64(),
                "list" => value[1].EnumerateArray().Select(item => ReadValue(item, depth + 1)).ToList(),
                "map" => value[1]
                    .EnumerateObject()
                    .ToDictionary(item => item.Name, item => ReadValue(item.Value, depth + 1), StringComparer.Ordinal),
                _ => throw new ChannelSerializationException($"Unknown value tag '{tag}'."),
            };
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            throw new ChannelSerializationException("Malformed tagged channel value.", exception);
        }
    }
}
