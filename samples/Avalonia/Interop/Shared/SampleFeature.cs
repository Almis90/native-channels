using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using NativeChannels.Sample;

namespace ReferenceSamples;

internal static class SampleFeature
{
    public const string Title = "NativeChannels interoperability — Avalonia";

    public static void Configure(IServiceCollection services) =>
        services.AddNativeChannels(options =>
        {
            options.Modules.Add(SamplePlatform.CreateModule());
            options.Json.TypeInfoResolver = SampleJsonContext.Default;
        });

    public static SamplePage Create(AvaloniaChannelSession owner) => new InteropPage(owner);
}
