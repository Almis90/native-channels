using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace NativeChannels.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.UseNativeChannels(options =>
        {
            options.Modules.Add(CreateSampleModule());
            options.ViewFactories.Add(
                new(
                    "sample.button",
                    "dev.nativechannels.sample.ButtonViewFactory",
                    "MCButtonViewFactory",
                    "NativeChannelsSample"
                )
                {
#if WINDOWS
                    WindowsFactory = WindowsButtonFactory.Create,
#endif
                }
            );
            options.Json.TypeInfoResolver = SampleJsonContext.Default;
        });
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }

    internal static NativeModuleRegistration CreateSampleModule() =>
        new("dev.nativechannels.sample.SampleModule", "MCSampleModule", "NativeChannelsSample")
        {
#if WINDOWS
            WindowsFactory = () => new WindowsSampleModule(),
#endif
        };
}
