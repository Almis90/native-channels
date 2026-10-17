using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using NativeChannels;

namespace Currency.Android;

[Application]
public sealed class Application(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<App>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).UseNativeChannels(NativeCurrency.ConfigureServices);
    }
}
