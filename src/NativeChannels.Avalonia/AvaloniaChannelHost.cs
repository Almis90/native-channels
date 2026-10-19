using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace NativeChannels;

/// <summary>Creates independent sessions and owns their asynchronous application shutdown.</summary>
public sealed class AvaloniaChannelHost : IAsyncDisposable
{
    private readonly OwnedChannelHost _host;

    internal AvaloniaChannelHost(ServiceDescriptor[] services) => _host = new(services);

    public static AvaloniaChannelHost Create(Action<IServiceCollection> configureServices) =>
        new(Configure(configureServices));

    internal static ServiceDescriptor[] Configure(Action<IServiceCollection> configureServices) =>
        OwnedChannelHost.Configure(configureServices);

    public AvaloniaChannelSession CreateSession() => new(_host.CreateSession(new AvaloniaHostDispatcher()));

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

internal sealed class AvaloniaHostDispatcher : IHostDispatcher
{
    public bool HasThreadAccess => Dispatcher.UIThread.CheckAccess();

    public void VerifyAccess() => Dispatcher.UIThread.VerifyAccess();

    public async Task InvokeAsync(Func<Task> action) => await Dispatcher.UIThread.InvokeAsync(action);
}
