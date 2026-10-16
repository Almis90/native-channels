using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace Currency.AvaloniaAndroid;

[Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<App>(handle, ownership);
