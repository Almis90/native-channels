namespace NativeChannels;

public sealed class MethodCall(string method, object? arguments = null, ChannelSerializer? serializer = null)
{
    private readonly ChannelSerializer _serializer = serializer ?? new();
    public string Method { get; } = method;
    public object? Arguments { get; } = arguments;

    public T? GetArguments<T>() => _serializer.ConvertResult<T>(Arguments);

    public T? GetArgument<T>(string name)
    {
        if (Arguments is not IDictionary<string, object?> map || !map.TryGetValue(name, out var value))
        {
            throw new ChannelException("invalid_arguments", $"Missing argument '{name}'.");
        }

        return _serializer.ConvertResult<T>(value);
    }
}
