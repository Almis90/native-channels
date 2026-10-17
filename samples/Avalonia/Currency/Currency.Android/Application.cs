using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Currency.Android;

[Application]
public sealed class Application(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<App>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.SessionFactory = NativeCurrency.CreateSession;
        return base.CustomizeAppBuilder(builder);
    }
}
