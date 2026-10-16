using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace NativeChannels;

public static class NativeChannelsExtensions
{
    public static MauiAppBuilder UseNativeChannels(
        this MauiAppBuilder builder,
        Action<NativeChannelsOptions>? configure = null
    )
    {
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IChannelService)))
        {
            throw new InvalidOperationException("NativeChannels has already been registered.");
        }

        var options = new NativeChannelsOptions();
        configure?.Invoke(options);
        var modules = options.Modules.ToArray();
        builder.Services.AddSingleton(new NativeViewSettings(options));
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<PlatformChannelView, PlatformChannelViewHandler>()
        );
        var serializer = new ChannelSerializer(options.Json);
        builder.Services.AddSingleton<IBinaryMessenger>(_ => new BinaryMessenger(
            new NativeChannelTransport(modules),
            options.Limits
        ));
        builder.Services.AddSingleton<IChannelService>(services => new ChannelService(
            services.GetRequiredService<IBinaryMessenger>(),
            serializer,
            options.DefaultTimeout,
            options.Limits
        ));
        builder.Services.AddSingleton<IMauiInitializeService, Initializer>();
        return builder;
    }

    private sealed class Initializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => _ = services.GetRequiredService<IChannelService>();
    }
}
