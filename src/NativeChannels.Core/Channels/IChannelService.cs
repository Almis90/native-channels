namespace NativeChannels;

public interface IChannelService : IAsyncDisposable
{
    MethodChannel GetMethodChannel(string name, MethodChannelOptions? options = null);
    BasicMessageChannel GetBasicMessageChannel(string name, BasicMessageChannelOptions? options = null);
    EventChannel GetEventChannel(string name, EventChannelOptions? options = null);
}
