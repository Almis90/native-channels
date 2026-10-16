using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using NativeChannels;
using Plugin.Maui.PdfViewer;

namespace PdfViewer.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.UseNativeChannels(options => options.AddPdfViewer());
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
