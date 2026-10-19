using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace NativeChannels;

/// <summary>Owns independent channel sessions; await shutdown before stopping Uno's UI queues.</summary>
public sealed class UnoChannelHost : IAsyncDisposable
{
    private readonly OwnedChannelHost _host;

    private UnoChannelHost(OwnedChannelHost host) => _host = host;

    public static UnoChannelHost Create(Action<IServiceCollection> configureServices) =>
        new(OwnedChannelHost.Create(configureServices));

    public UnoChannelSession CreateSession(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var dispatcher = new UnoChannelDispatcher(window.DispatcherQueue);
        return new(_host.CreateSession(dispatcher), dispatcher);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

/// <summary>Owns a provider, channels and cancellation lifetime for one window.</summary>
public sealed class UnoChannelSession : IAsyncDisposable
{
    private readonly OwnedChannelSession _session;

    internal UnoChannelSession(OwnedChannelSession session, UnoChannelDispatcher dispatcher)
    {
        _session = session;
        Dispatcher = dispatcher;
    }

    public UnoChannelDispatcher Dispatcher { get; }
    public CancellationToken Stopping => _session.Stopping;
    public IServiceProvider Services => _session.Services;
    public IChannelService Channels => _session.Channels;

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}

/// <summary>Startup integration without requiring Uno navigation or application architecture packages.</summary>
public static class UnoChannelExtensions
{
    private static readonly ConditionalWeakTable<Application, UnoChannelHost> Hosts = new();

    public static UnoChannelHost UseNativeChannels(
        this Application application,
        Action<IServiceCollection> configureServices
    )
    {
        ArgumentNullException.ThrowIfNull(application);
        lock (Hosts)
        {
            if (Hosts.TryGetValue(application, out _))
                throw new InvalidOperationException("NativeChannels is already configured for this Uno application.");
            var host = UnoChannelHost.Create(configureServices);
            Hosts.Add(application, host);
            return host;
        }
    }

    public static UnoChannelHost GetNativeChannels(this Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return Hosts.TryGetValue(application, out var host)
            ? host
            : throw new InvalidOperationException("Call UseNativeChannels during Uno startup.");
    }
}
