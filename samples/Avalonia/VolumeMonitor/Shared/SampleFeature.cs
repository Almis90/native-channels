using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.VolumeMonitor;

namespace ReferenceSamples;

internal static class SampleFeature
{
    public const string Title = "VolumeMonitor — Avalonia";

    public static void Configure(IServiceCollection services)
    {
        services.AddNativeChannels(options => options.AddVolumeMonitor());
        services.AddVolumeMonitor();
    }

    public static SamplePage Create(AvaloniaChannelSession owner) => new VolumePage(owner);
}
