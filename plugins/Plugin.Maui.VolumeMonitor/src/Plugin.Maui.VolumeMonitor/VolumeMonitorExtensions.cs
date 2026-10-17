using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.Maui.VolumeMonitor;

public static class VolumeMonitorExtensions
{
    /// <summary>Adds the plugin's packaged native module to a NativeChannels session.</summary>
    public static TOptions AddVolumeMonitor<TOptions>(this TOptions options)
        where TOptions : ChannelRegistrationOptions
    {
        ArgumentNullException.ThrowIfNull(options);
        var module = new NativeModuleRegistration(
            "dev.nativechannels.volume.VolumeMonitorModule",
            "MCVolumeMonitorModule",
            "MauiVolumeMonitorNative"
        )
        {
#if BROWSER
            BrowserModule = "./_content/Plugin.Maui.VolumeMonitor/volumemonitor.mjs",
#endif
#if LINUX
            LinuxFactory = CreateLinuxModule,
#endif
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

#if LINUX
    private static ILinuxChannelModule CreateLinuxModule() =>
        new LinuxNativeModule(
            "libnc_volume.so",
            typeof(VolumeMonitorExtensions).Assembly,
            [],
            eventChannels: [VolumeMonitor.ChannelName]
        );
#endif
#if WINDOWS
    private static IWindowsChannelModule CreateWindowsModule() => new WindowsVolumeMonitorModule();
#endif
}
