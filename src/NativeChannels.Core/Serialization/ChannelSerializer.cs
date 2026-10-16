using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NativeChannels;

/// <summary>Converts application DTOs using explicitly supplied JSON metadata, without runtime reflection.</summary>
public sealed class ChannelSerializer(JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions _options = options ?? new(JsonSerializerDefaults.Web);

    internal object? Normalize(object? value, int depth = 0)
    {
        if (depth > 64)
        {
            throw new ChannelSerializationException("Object graph is too deep or cyclic.");
        }

        if (value is null or bool or string or int or long or double or byte[])
        {
            return value;
        }

        if (value is byte or sbyte or short or ushort)
        {
            return Convert.ToInt32(value);
        }

        if (value is uint unsigned)
        {
            return (long)unsigned;
        }

        if (value is float single)
        {
            return (double)single;
        }

        if (value is DateTime date)
        {
            return date.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset offset)
        {
            return offset.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is JsonElement element)
        {
            return FromJson(element, depth);
        }

        if (value is IDictionary dictionary)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new ChannelSerializationException("Map keys must be strings.");
                }

                map.Add(key, Normalize(entry.Value, depth + 1));
            }
            return map;
        }
        if (value is IEnumerable sequence)
        {
            return sequence.Cast<object?>().Select(item => Normalize(item, depth + 1)).ToList();
        }

        var metadata = GetMetadata(value.GetType());
        if (metadata.Kind != JsonTypeInfoKind.Object)
        {
            throw new ChannelSerializationException($"Unsupported value type: {value.GetType().Name}.");
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in metadata.Properties)
        {
            if (property.Get is not null)
            {
                result.Add(property.Name, Normalize(property.Get(value), depth + 1));
            }
        }

        return result;
    }

    public T? ConvertResult<T>(object? value)
    {
        if (value is null)
        {
            if (default(T) is not null)
            {
                throw new ChannelSerializationException($"A null result cannot be assigned to {typeof(T).Name}.");
            }

            return default;
        }
        if (value is T result)
        {
            return result;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            object? converted =
                target == typeof(long) && value is int integer ? (long)integer
                : target == typeof(int) && value is long wide ? checked((int)wide)
                : target == typeof(double) && value is int number ? (double)number
                : null;
            if (converted is not null)
            {
                return (T)converted;
            }

            ValidateNumericConversions(value, target);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WritePlain(writer, value);
            }

            return (T?)JsonSerializer.Deserialize(buffer.ToArray(), GetMetadata(typeof(T)));
        }
        catch (Exception exception)
            when (exception is JsonException or OverflowException or InvalidCastException or NotSupportedException)
        {
            throw new ChannelSerializationException($"Cannot deserialize result as {typeof(T).Name}.", exception);
        }
    }

    private void ValidateNumericConversions(object? value, Type target)
    {
        if (value is null || target == typeof(object))
        {
            return;
        }

        target = Nullable.GetUnderlyingType(target) ?? target;
        if (value is int or long or double)
        {
            if (target == typeof(decimal) || target == typeof(ulong) || target == typeof(uint))
            {
                throw new ChannelSerializationException($"Unsupported numeric target {target.Name}.");
            }

            if (WouldLoseNumericPrecision(value, target))
            {
                throw new ChannelSerializationException("Numeric conversion would lose precision.");
            }

            return;
        }
        if (value is IDictionary<string, object?> map)
        {
            var metadata = GetMetadata(target);
            if (metadata.Kind == JsonTypeInfoKind.Dictionary && metadata.ElementType is { } element)
            {
                foreach (var item in map.Values)
                {
                    ValidateNumericConversions(item, element);
                }
            }
            else if (metadata.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var item in map)
                {
                    var comparison = _options.PropertyNameCaseInsensitive
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                    var property = metadata.Properties.FirstOrDefault(property =>
                        string.Equals(property.Name, item.Key, comparison)
                    );
                    if (property is not null)
                    {
                        ValidateNumericConversions(item.Value, property.PropertyType);
                    }
                }
            }
        }
        else if (value is IEnumerable<object?> list && GetMetadata(target).ElementType is { } element)
        {
            foreach (var item in list)
            {
                ValidateNumericConversions(item, element);
            }
        }
    }

    private static bool WouldLoseNumericPrecision(object value, Type target) =>
        value switch
        {
            long integer when target == typeof(double) => checked((long)(double)integer) != integer,
            long integer when target == typeof(float) => checked((long)(float)integer) != integer,
            int integer when target == typeof(float) => checked((int)(float)integer) != integer,
            double number when target == typeof(float) => (double)(float)number != number,
            double number when target == typeof(long) => (double)checked((long)number) != number,
            double number when target == typeof(int) => (double)checked((int)number) != number,
            _ => false,
        };

    private JsonTypeInfo GetMetadata(Type type)
    {
        try
        {
            return _options.GetTypeInfo(type);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            throw new ChannelSerializationException(
                $"Register source-generated JsonSerializerContext metadata for {type.Name}.",
                exception
            );
        }
    }

    private object? FromJson(JsonElement value, int depth) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out var integer) => integer,
            JsonValueKind.Number when value.TryGetInt64(out var wide) => wide,
            JsonValueKind.Number when value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0 =>
                throw new ChannelSerializationException("JSON integer exceeds the Int64 range."),
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Array => value.EnumerateArray().Select(item => Normalize(item, depth + 1)).ToList(),
            JsonValueKind.Object => value
                .EnumerateObject()
                .ToDictionary(item => item.Name, item => Normalize(item.Value, depth + 1), StringComparer.Ordinal),
            _ => throw new ChannelSerializationException("Unsupported JSON value."),
        };

    private static void WritePlain(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case int integer:
                writer.WriteNumberValue(integer);
                break;
            case long integer:
                writer.WriteNumberValue(integer);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            case IDictionary<string, object?> map:
                writer.WriteStartObject();
                foreach (var item in map)
                {
                    writer.WritePropertyName(item.Key);
                    WritePlain(writer, item.Value);
                }
                writer.WriteEndObject();
                break;
            case IEnumerable<object?> sequence:
                writer.WriteStartArray();
                foreach (var item in sequence)
                {
                    WritePlain(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new ChannelSerializationException("Invalid canonical value.");
        }
    }
}
