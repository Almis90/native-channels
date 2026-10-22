using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace NativeChannels;

/// <summary>Owns an Uno hosting container for a borrowed AppKit NSView. Use on the UI thread.</summary>
public sealed class MacOSNativeView : IDisposable
{
    // TODO: Replace this private Uno 6.7.135 contract with its public NSView hosting API.
    // https://github.com/unoplatform/uno/issues/25639
    private readonly PropertyInfo _nativeHandle;
    private readonly UnoChannelDispatcher _dispatcher;
    private nint _container;
    public object NativeElement { get; }

    public MacOSNativeView(nint view)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("AppKit hosting requires macOS.");
        _dispatcher = new UnoChannelDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _dispatcher.VerifyAccess();
        if (view == 0)
            throw new ArgumentException("An NSView is required.", nameof(view));
        var type = Type.GetType("Uno.UI.Runtime.Skia.MacOS.MacOSNativeElement, Uno.UI.Runtime.Skia.MacOS", true)!;
        _nativeHandle =
            type.GetProperty("NativeHandle")
            ?? throw new PlatformNotSupportedException("Unsupported Uno macOS native wrapper.");
        NativeElement = Activator.CreateInstance(type, true)!;
        _container = Create(view);
        if (_container == 0)
            throw new InvalidOperationException("The NSView must be unattached and created on the UI thread.");
        try
        {
            _nativeHandle.SetValue(NativeElement, _container);
        }
        catch
        {
            Release(_container);
            _container = 0;
            throw;
        }
    }

    internal static void Prepare(object element, XamlRoot root)
    {
        if (element.GetType().FullName != "Uno.UI.Runtime.Skia.MacOS.MacOSNativeElement")
            return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var window =
            typeof(XamlRoot).GetProperty("HostWindow", flags)?.GetValue(root)
            ?? throw new PlatformNotSupportedException("Unsupported Uno macOS window contract.");
        var native = typeof(Window).GetProperty("NativeWindow", flags)?.GetValue(window);
        var handle = native is nint pointer
            ? pointer
            : (nint?)native?.GetType().GetProperty("Handle", flags)?.GetValue(native) ?? 0;
        var container = (nint)element.GetType().GetProperty("NativeHandle")!.GetValue(element)!;
        if (SetParent(container, handle) == 0)
            throw new PlatformNotSupportedException("Uno macOS rendering view is unavailable.");
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_container == 0)
            return;
        // Uno 6.7 keeps separate attached/transient sets. Drain both before release.
        Detach(_container);
        DisposeNative(_container);
        _nativeHandle.SetValue(NativeElement, (nint)0);
        Release(_container);
        _container = 0;
    }

    [DllImport("NativeChannelsUno", EntryPoint = "nc_uno_create")]
    private static extern nint Create(nint view);

    [DllImport("NativeChannelsUno", EntryPoint = "nc_uno_parent")]
    private static extern int SetParent(nint container, nint window);

    [DllImport("NativeChannelsUno", EntryPoint = "nc_uno_release")]
    private static extern void Release(nint container);

    [DllImport("UnoNativeMac", EntryPoint = "uno_native_detach")]
    private static extern void Detach(nint container);

    [DllImport("UnoNativeMac", EntryPoint = "uno_native_dispose")]
    private static extern void DisposeNative(nint container);
}
