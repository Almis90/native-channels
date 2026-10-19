namespace NativeChannels;

/// <summary>Registers Linux channel endpoints, including native modules hosted by LinuxNativeModule.</summary>
public interface ILinuxChannelModule : IDisposable
{
    void Register(IChannelService channels);
}
