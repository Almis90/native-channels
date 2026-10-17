using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeChannels;

namespace Plugin.Maui.AudioStreamer;

public static class AudioStreamerExtensions
{
    /// <summary>Adds the plugin's packaged native module to a NativeChannels session.</summary>
    public static TOptions AddAudioStreamer<TOptions>(this TOptions options)
        where TOptions : ChannelRegistrationOptions
    {
        ArgumentNullException.ThrowIfNull(options);
        var module = new NativeModuleRegistration(
            "dev.nativechannels.audio.AudioStreamerModule",
            "MCAudioStreamerModule",
            "MauiAudioStreamerNative"
        )
        {
#if LINUX
            LinuxFactory = CreateLinuxModule,
#endif
#if BROWSER
            BrowserModule = "./_content/Plugin.Maui.AudioStreamer/audiostreamer.mjs",
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

    /// <summary>Registers the audio streamer. Also call options.AddAudioStreamer when registering the channel session.</summary>
    public static IServiceCollection AddAudioStreamer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAudioStreamer, AudioStreamer>();
        return services;
    }

#if LINUX
    private static ILinuxChannelModule CreateLinuxModule() =>
        new LinuxNativeModule(
            "libnc_audio.so",
            typeof(AudioStreamerExtensions).Assembly,
            [AudioFormat.ControlChannel],
            [AudioFormat.DataChannel]
        );
#endif
#if WINDOWS
    private static IWindowsChannelModule CreateWindowsModule() => new WindowsAudioStreamerModule();
#endif
}
