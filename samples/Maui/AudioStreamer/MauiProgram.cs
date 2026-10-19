using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using NativeChannels;
using Plugin.AudioStreamer;

namespace AudioStreamer.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.UseNativeChannels(options => options.AddAudioStreamer());
        builder.Services.AddAudioStreamer();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
