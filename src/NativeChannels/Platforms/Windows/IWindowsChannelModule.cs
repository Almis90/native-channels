namespace NativeChannels;

/// <summary>A Windows adapter that accesses WinRT, COM, or native SDKs through C#.</summary>
public interface IWindowsChannelModule : IDisposable
{
    /// <summary>Registers handlers on the adapter endpoint. Called once for each new module instance.</summary>
    void Register(IChannelService channels);
}
