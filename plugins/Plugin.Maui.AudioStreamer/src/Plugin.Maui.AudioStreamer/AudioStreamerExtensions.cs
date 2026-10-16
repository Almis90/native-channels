using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.Maui.AudioStreamer;

public static class AudioStreamerExtensions
{
    /// <summary>Adds the plugin's packaged native module to a NativeChannels session.</summary>
    public static NativeChannelsOptions AddAudioStreamer(this NativeChannelsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var module = new NativeModuleRegistration(
            "dev.nativechannels.audio.AudioStreamerModule",
            "MCAudioStreamerModule",
            "MauiAudioStreamerNative"
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

    /// <summary>Registers the audio streamer. Also call options.AddAudioStreamer inside UseNativeChannels.</summary>
    public static IServiceCollection AddAudioStreamer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAudioStreamer, AudioStreamer>();
        return services;
    }

#if WINDOWS
    private static IWindowsChannelModule CreateWindowsModule() => new WindowsAudioStreamerModule();
#endif
}
