using System.Runtime.CompilerServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace NativeChannels;

/// <summary>Associates one reusable host with an Avalonia application.</summary>
public static class AvaloniaChannelExtensions
{
    private static readonly ConditionalWeakTable<AppBuilder, object> Builders = new();
    private static readonly ConditionalWeakTable<Application, AvaloniaChannelHost> Hosts = new();

    /// <summary>Configures services once; each created session receives its own provider.</summary>
    public static AppBuilder UseNativeChannels(this AppBuilder builder, Action<IServiceCollection> configureServices)
    {
        ArgumentNullException.ThrowIfNull(builder);
        lock (Builders)
        {
            if (Builders.TryGetValue(builder, out _))
                throw new InvalidOperationException(
                    "NativeChannels has already been configured for this Avalonia builder."
                );
            var services = AvaloniaChannelHost.Configure(configureServices);
            Builders.Add(builder, new object());
            return builder.AfterSetup(configured => Hosts.Add(configured.Instance!, new AvaloniaChannelHost(services)));
        }
    }

    /// <summary>Available from OnFrameworkInitializationCompleted onward. Await disposal before exiting the application.</summary>
    public static AvaloniaChannelHost GetNativeChannels(this Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return Hosts.TryGetValue(application, out var host)
            ? host
            : throw new InvalidOperationException(
                "Call UseNativeChannels on the Avalonia AppBuilder before accessing the host."
            );
    }
}
