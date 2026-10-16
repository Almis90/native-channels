using System.Text.Json;

namespace NativeChannels;

public sealed class JsonMethodCodec(JsonMessageCodec? values = null) : IMethodCodec
{
    private readonly JsonMessageCodec _values = values ?? new();

    public ReadOnlyMemory<byte> EncodeCall(MethodCall call)
    {
        _values.Limits.ValidateName(call.Method);
        return Envelope(writer =>
        {
            writer.WriteString("method", call.Method);
            writer.WritePropertyName("arguments");
            _values.WriteValue(writer, _values.Serializer.Normalize(call.Arguments));
        });
    }

    public MethodCall DecodeCall(ReadOnlyMemory<byte> payload) =>
        Read(
            payload,
            root =>
            {
                var method = root.GetProperty("method").GetString()!;
                _values.Limits.ValidateName(method);
                return new MethodCall(method, _values.ReadValue(root.GetProperty("arguments")), _values.Serializer);
            }
        );

    public ReadOnlyMemory<byte> EncodeSuccess(object? value) =>
        Envelope(writer =>
        {
            writer.WriteString("status", "success");
            writer.WritePropertyName("value");
            _values.WriteValue(writer, _values.Serializer.Normalize(value));
        });

    public ReadOnlyMemory<byte> EncodeError(ChannelException error) =>
        Envelope(writer =>
        {
            writer.WriteString("status", "error");
            writer.WriteString("code", error.Code);
            writer.WriteString("message", error.Message);
            writer.WriteString("platform", error.Platform);
            writer.WritePropertyName("details");
            _values.WriteValue(writer, _values.Serializer.Normalize(error.Details));
        });

    public ReadOnlyMemory<byte> EncodeNotImplemented() =>
        Envelope(writer => writer.WriteString("status", "not_implemented"));

    public object? DecodeResult(ReadOnlyMemory<byte> payload) =>
        Read<object?>(
            payload,
            root =>
                root.GetProperty("status").GetString() switch
                {
                    "success" => _values.ReadValue(root.GetProperty("value")),
                    "not_implemented" => throw new MethodNotImplementedException(),
                    "error" => throw new ChannelException(
                        RequiredString(root, "code"),
                        RequiredString(root, "message"),
                        _values.ReadValue(root.GetProperty("details")),
                        root.GetProperty("platform").GetString()
                    ),
                    _ => throw new ChannelSerializationException("Unknown method result status."),
                }
        );

    private static string RequiredString(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new ChannelSerializationException($"Missing error {name}.");

    private ReadOnlyMemory<byte> Envelope(Action<Utf8JsonWriter> body) =>
        _values.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);
            body(writer);
            writer.WriteEndObject();
        });

    private T Read<T>(ReadOnlyMemory<byte> payload, Func<JsonElement, T> decode)
    {
        using var document = _values.Parse(payload);
        try
        {
            var root = document.RootElement;
            if (
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count()
                != root.EnumerateObject().Count()
            )
            {
                throw new ChannelSerializationException("Duplicate envelope property.");
            }

            if (root.GetProperty("v").GetInt32() != 1)
            {
                throw new ChannelSerializationException("Unsupported protocol version.");
            }

            return decode(root);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException
            )
        {
            throw new ChannelSerializationException("Invalid method envelope.", exception);
        }
    }
}
