namespace NativeChannels;

/// <summary>Shared session configuration plus MAUI native-view factories.</summary>
public sealed class NativeChannelsOptions : ChannelRegistrationOptions
{
    public IList<NativeViewFactoryRegistration> ViewFactories { get; } = new List<NativeViewFactoryRegistration>();
}
