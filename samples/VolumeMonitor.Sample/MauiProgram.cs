using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using NativeChannels;
using Plugin.Maui.VolumeMonitor;

namespace VolumeMonitor.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.UseNativeChannels(options => options.AddVolumeMonitor());
        builder.Services.AddVolumeMonitor();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
