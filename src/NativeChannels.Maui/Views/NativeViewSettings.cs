namespace NativeChannels;

internal sealed class NativeViewSettings
{
    private readonly Dictionary<string, NativeViewFactoryRegistration> _factories = new(StringComparer.Ordinal);
    internal ChannelSerializer Serializer { get; }
    internal ChannelLimits Limits { get; }
    internal TimeSpan Timeout { get; }
    internal JsonMessageCodec Codec { get; }

    internal NativeViewSettings(NativeChannelsOptions options)
    {
        Serializer = new(options.Json);
        Limits = options.Limits;
        Timeout = options.DefaultTimeout;
        Codec = new(Serializer, Limits);
        foreach (var factory in options.ViewFactories)
        {
            if (
                string.IsNullOrWhiteSpace(factory.Name)
                || System.Text.Encoding.UTF8.GetByteCount(factory.Name) > 256
                || factory.Name.Any(char.IsControl)
            )
                throw new ArgumentException("View factory names must be nonempty and at most 256 UTF-8 bytes.");
            if (!_factories.TryAdd(factory.Name, factory))
                throw new ArgumentException($"Duplicate native view factory '{factory.Name}'.");
        }
    }

    internal NativeViewFactoryRegistration GetFactory(string name) =>
        _factories.TryGetValue(name, out var factory)
            ? factory
            : throw new InvalidOperationException(
                $"Native view factory '{name}' is not registered. Add it to UseNativeChannels(options => options.ViewFactories.Add(...)) and package its native adapter."
            );
}
