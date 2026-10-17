using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using NativeChannels;

namespace ReferenceSamples;

[Application]
public sealed class Application(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<SampleApplication>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).UseNativeChannels(SampleFeature.Configure);
}
