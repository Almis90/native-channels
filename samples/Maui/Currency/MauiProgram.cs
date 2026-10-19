using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using NativeChannels;
using Plugin.Currency;

namespace Currency.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.UseNativeChannels(options => options.AddCurrency());
        builder.Services.AddCurrency();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
