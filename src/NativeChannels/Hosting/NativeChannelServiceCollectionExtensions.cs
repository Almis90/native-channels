using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace NativeChannels;

/// <summary>Registers an owned native session without selecting an application framework.</summary>
public static class NativeChannelServiceCollectionExtensions
{
    /// <summary>Registers one lazy session per service provider. The host resolves and disposes its services.</summary>
    public static IServiceCollection AddNativeChannels(
        this IServiceCollection services,
        Action<ChannelRegistrationOptions>? configure = null
    )
    {
        CheckRegistration(services);
        var options = new ChannelRegistrationOptions();
        configure?.Invoke(options);
        return services.AddNativeChannels(options);
    }

    /// <summary>Snapshots shared settings, including settings supplied by an application host.</summary>
    public static IServiceCollection AddNativeChannels(
        this IServiceCollection services,
        ChannelRegistrationOptions options
    )
    {
        CheckRegistration(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Limits);
        ArgumentNullException.ThrowIfNull(options.Json);
        var limits = options.Limits;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaximumNameBytes);
        var timeout = options.DefaultTimeout;
        if (
            timeout != Timeout.InfiniteTimeSpan
            && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Use a positive supported timer interval or Timeout.InfiniteTimeSpan."
            );
        }

        var modules = options.Modules.ToArray();
        var serializer = new ChannelSerializer(new JsonSerializerOptions(options.Json));
        services.AddSingleton<IBinaryMessenger>(_ => new BinaryMessenger(new NativeChannelTransport(modules), limits));
        services.AddSingleton<IChannelService>(provider => new ChannelService(
            provider.GetRequiredService<IBinaryMessenger>(),
            serializer,
            timeout,
            limits
        ));
        return services;
    }

    private static void CheckRegistration(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (
            services.Any(descriptor =>
                descriptor.ServiceType == typeof(IChannelService) || descriptor.ServiceType == typeof(IBinaryMessenger)
            )
        )
        {
            throw new InvalidOperationException("NativeChannels has already been registered.");
        }
    }
}
