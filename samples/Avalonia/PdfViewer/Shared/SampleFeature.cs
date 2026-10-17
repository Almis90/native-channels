using Microsoft.Extensions.DependencyInjection;
using NativeChannels;

namespace ReferenceSamples;

internal static class SampleFeature
{
    public const string Title = "PDF native views — Avalonia";

    public static void Configure(IServiceCollection services) => services.AddNativeChannels();

    public static SamplePage Create(AvaloniaChannelSession owner) => new PdfPage(owner);
}
