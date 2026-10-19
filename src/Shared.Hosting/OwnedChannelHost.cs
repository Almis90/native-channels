using Microsoft.Extensions.DependencyInjection;

namespace NativeChannels;

/// <summary>Creates independent sessions and owns their asynchronous application shutdown.</summary>
internal sealed class OwnedChannelHost : IAsyncDisposable
{
    private readonly ServiceDescriptor[] _services;
    private readonly HashSet<OwnedChannelSession> _sessions = [];
    private readonly object _gate = new();
    private Task? _disposal;

    internal OwnedChannelHost(ServiceDescriptor[] services) => _services = services;

    /// <summary>Creates a manually owned host. Platform registration belongs in configureServices.</summary>
    public static OwnedChannelHost Create(Action<IServiceCollection> configureServices) =>
        new(Configure(configureServices));

    internal static ServiceDescriptor[] Configure(Action<IServiceCollection> configureServices)
    {
        ArgumentNullException.ThrowIfNull(configureServices);
        var services = new ServiceCollection();
        configureServices(services);
        if (!services.Any(service => service.ServiceType == typeof(IChannelService)))
            throw new InvalidOperationException(
                "Register NativeChannels with services.AddNativeChannels(...) before creating a UI host."
            );
        if (services.Count(service => service.ServiceType == typeof(IChannelService)) != 1)
            throw new InvalidOperationException("Register exactly one IChannelService for each UI session.");
        var channels = services.Single(service => service.ServiceType == typeof(IChannelService));
        if (
            channels.IsKeyedService
            || channels.Lifetime != ServiceLifetime.Singleton
            || channels.ImplementationInstance is not null
        )
            throw new InvalidOperationException(
                "Register IChannelService with an unkeyed singleton factory or type so each session owns its channel service."
            );
        return services.ToArray();
    }

    /// <summary>Creates a session on the UI thread. Dispose it when its window or activity is permanently closed.</summary>
    public OwnedChannelSession CreateSession(IHostDispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            IServiceCollection services = new ServiceCollection();
            foreach (var descriptor in _services)
                services.Add(descriptor);
            // Validate without constructing platform adapters. The session owns services even when
            // a later factory throws during resolution, and can always be asynchronously disposed.
            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
            var session = new OwnedChannelSession(provider, dispatcher, Release);
            _sessions.Add(session);
            return session;
        }
    }

    private void Release(OwnedChannelSession session)
    {
        lock (_gate)
            _sessions.Remove(session);
    }

    /// <summary>Prevents new sessions immediately and awaits every session, including cleanup failures.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        OwnedChannelSession[] sessions;
        lock (_gate)
        {
            if (_disposal is not null)
                return new(_disposal);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = completion.Task;
            sessions = [.. _sessions];
        }
        _ = CompleteDisposalAsync(sessions, completion);
        return new(completion.Task);
    }

    private static async Task CompleteDisposalAsync(OwnedChannelSession[] sessions, TaskCompletionSource completion)
    {
        var pending = Task.WhenAll(sessions.Select(session => session.DisposeAsync().AsTask()));
        try
        {
            await pending;
            completion.SetResult();
        }
        catch
        {
            completion.SetException(pending.Exception!.InnerExceptions);
        }
    }
}
