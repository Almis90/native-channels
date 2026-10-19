using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.VolumeMonitor;

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
            BrowserModule = "./_content/Plugin.VolumeMonitor/volumemonitor.mjs",
#endif
#if LINUX || DESKTOP
            LinuxFactory = CreateLinuxModule,
#endif
#if WINDOWS || DESKTOP
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

#if LINUX || DESKTOP
    private static ILinuxChannelModule CreateLinuxModule() =>
        new LinuxNativeModule(
            "libnc_volume.so",
            typeof(VolumeMonitorExtensions).Assembly,
            [],
            eventChannels: [VolumeMonitor.ChannelName]
        );
#endif
#if WINDOWS || DESKTOP
    private static IWindowsChannelModule CreateWindowsModule() =>
        OperatingSystem.IsWindows()
            ? new WindowsVolumeMonitorModule()
            : throw new PlatformNotSupportedException("The Windows volume adapter requires Windows.");
#endif
}
