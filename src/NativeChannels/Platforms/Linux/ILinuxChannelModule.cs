#if LINUX
namespace NativeChannels;

/// <summary>A Linux adapter calling system APIs through .NET interop.</summary>
public interface ILinuxChannelModule : IDisposable
{
    void Register(IChannelService channels);
}
#endif
