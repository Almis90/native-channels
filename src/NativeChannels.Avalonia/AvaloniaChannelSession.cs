namespace NativeChannels;

/// <summary>An independent channel session and service provider, owned by an Avalonia host.</summary>
public sealed class AvaloniaChannelSession : IAsyncDisposable
{
    private readonly OwnedChannelSession _session;

    internal AvaloniaChannelSession(OwnedChannelSession session) => _session = session;

    public CancellationToken Stopping => _session.Stopping;
    public IServiceProvider Services => _session.Services;
    public IChannelService Channels => _session.Channels;

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
