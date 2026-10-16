using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.Maui.VolumeMonitor;

public static class VolumeMonitorExtensions
{
    /// <summary>Adds the plugin's packaged native module to a NativeChannels session.</summary>
    public static NativeChannelsOptions AddVolumeMonitor(this NativeChannelsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var module = new NativeModuleRegistration(
            "dev.nativechannels.volume.VolumeMonitorModule",
            "MCVolumeMonitorModule",
            "MauiVolumeMonitorNative"
        )
        {
#if WINDOWS
            WindowsFactory = CreateWindowsModule,
#endif
        };
        if (!options.Modules.Contains(module))
        {
            options.Modules.Add(module);
        }
        return options;
    }

    /// <summary>Registers the volume monitor. Also call options.AddVolumeMonitor inside UseNativeChannels.</summary>
    public static IServiceCollection AddVolumeMonitor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IVolumeMonitor, VolumeMonitor>();
        return services;
    }

#if WINDOWS
    private static IWindowsChannelModule CreateWindowsModule() => new WindowsVolumeMonitorModule();
#endif
}
