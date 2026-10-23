using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.AudioStreamer;

namespace ReferenceSamples;

internal static class SampleFeature
{
    public const string Title = "AudioStreamer — Avalonia";

    public static void Configure(IServiceCollection services)
    {
        services.AddNativeChannels(options => options.AddAudioStreamer());
        services.AddAudioStreamer();
    }

    public static SamplePage Create(AvaloniaChannelSession owner) => new AudioPage(owner);
}
