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
        ArgumentNullException.ThrowIfNull(builder);
        if (
            builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(IChannelService) || descriptor.ServiceType == typeof(IBinaryMessenger)
            )
        )
        {
            throw new InvalidOperationException("NativeChannels has already been registered.");
        }

        var options = new NativeChannelsOptions();
        configure?.Invoke(options);
        var views = new NativeViewSettings(options);
        builder.Services.AddNativeChannels(options);
        builder.Services.AddSingleton(views);
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<PlatformChannelView, PlatformChannelViewHandler>()
        );
        builder.Services.AddSingleton<IMauiInitializeService, Initializer>();
        return builder;
    }

    private sealed class Initializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => _ = services.GetRequiredService<IChannelService>();
    }
}
