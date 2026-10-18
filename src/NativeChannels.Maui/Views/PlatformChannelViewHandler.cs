using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
#if ANDROID
using PlatformContainer = Android.Widget.FrameLayout;
using NativeHost = NativeChannels.Native.Android.NativeViewHost;
#elif WINDOWS
using PlatformContainer = Microsoft.UI.Xaml.Controls.Grid;
using NativeHost = NativeChannels.WindowsNativeView;
#else
using PlatformContainer = UIKit.UIView;
using NativeHost = NativeChannels.Native.Apple.NativeViewHost;
#endif

namespace NativeChannels;

public sealed class PlatformChannelViewHandler : ViewHandler<PlatformChannelView, PlatformContainer>
{
    private ViewChannelLease? _lease;
    private NativeHost? _host;
    private Task _cleanup = Task.CompletedTask;

    public PlatformChannelViewHandler()
        : base(ViewMapper) { }

    protected override PlatformContainer CreatePlatformView()
    {
        PlatformChannelView.RequireMainThread();
#if ANDROID
        return new NativeViewContainer(Context);
#else
        return new();
#endif
    }

    protected override void ConnectHandler(PlatformContainer platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.Loaded += OnLoaded;
        VirtualView.Unloaded += OnUnloaded;
        ConnectView();
    }

    protected override void DisconnectHandler(PlatformContainer platformView)
    {
        VirtualView.Loaded -= OnLoaded;
        VirtualView.Unloaded -= OnUnloaded;
        Observe(DisconnectView());
        base.DisconnectHandler(platformView);
    }

    private void OnLoaded(object? sender, EventArgs e) => ConnectView();

    private void OnUnloaded(object? sender, EventArgs e) => Observe(DisconnectView());

    internal void ReconnectView()
    {
        PlatformChannelView.RequireMainThread();
        Observe(DisconnectView());
        ConnectView();
    }

    private void ConnectView()
    {
        PlatformChannelView.RequireMainThread();
        if (_lease is not null)
            return;
        var settings = MauiContext!.Services.GetRequiredService<NativeViewSettings>();
        var factory = settings.GetFactory(VirtualView.FactoryName);
        var arguments = settings.Codec.Encode(VirtualView.CreationParameters).ToArray();
        var transport = new NativeChannelTransport();
        ViewChannelLease? lease = null;
        NativeHost? host = null;
        try
        {
            lease = new ViewChannelLease(
                transport,
                settings.Serializer,
                settings.Timeout,
                settings.Limits,
                MauiChannelDispatchers.MainThread,
                settings.Diagnostics
            );
#if ANDROID
            host = transport.CreateView(
                new(factory.AndroidClass, factory.AppleClass, factory.AppleFramework),
                Context,
                lease.InstanceId,
                lease.Channel.Name,
                arguments
            );
            PlatformView.AddView(host.View, new Android.Widget.FrameLayout.LayoutParams(-1, -1));
#elif WINDOWS
            var create =
                factory.WindowsFactory
                ?? throw new PlatformNotSupportedException($"View factory '{factory.Name}' has no WindowsFactory.");
            host =
                create(
                    new(
                        transport.WindowsChannels,
                        lease.InstanceId,
                        lease.Channel.Name,
                        settings.Codec.Decode(arguments)
                    )
                ) ?? throw new InvalidOperationException("Windows view factory returned no view.");
            PlatformView.Children.Add(host.View);
#else
            host = transport.CreateView(
                new(factory.AndroidClass, factory.AppleClass, factory.AppleFramework),
                lease.InstanceId,
                lease.Channel.Name,
                arguments
            );
            var child = host.View ?? throw new InvalidOperationException("Native factory returned no view.");
            child.Frame = PlatformView.Bounds;
            child.AutoresizingMask = UIKit.UIViewAutoresizing.FlexibleWidth | UIKit.UIViewAutoresizing.FlexibleHeight;
            PlatformView.AddSubview(child);
#endif
            _host = host;
            _lease = lease;
            VirtualView.SetConnection(lease);
        }
        catch
        {
            _host = null;
            _lease = null;
            try
            {
                RemoveAndClose(host);
            }
            finally
            {
                Observe(lease is null ? transport.DisposeAsync().AsTask() : lease.DisposeAsync().AsTask());
                if (VirtualView.Channel is not null)
                    VirtualView.SetConnection(null);
            }
            throw;
        }
    }

    internal Task DisconnectView()
    {
        PlatformChannelView.RequireMainThread();
        var lease = _lease;
        if (lease is null)
            return _cleanup;
        _lease = null;
        var host = _host;
        _host = null;
        // Invalidate callers before detaching listeners and releasing the native view.
        lease.Channel.Dispose();
        try
        {
            RemoveAndClose(host);
        }
        finally
        {
            _cleanup = Task.WhenAll(_cleanup, lease.DisposeAsync().AsTask());
            VirtualView.SetConnection(null);
        }
        return _cleanup;
    }

    private void RemoveAndClose(NativeHost? host)
    {
        try
        {
            RemoveNativeContent();
        }
        finally
        {
            try
            {
                host?.Close();
            }
            finally
            {
                host?.Dispose();
            }
        }
    }

    private void RemoveNativeContent()
    {
#if ANDROID
        PlatformView.RemoveAllViews();
#elif WINDOWS
        PlatformView.Children.Clear();
#else
        foreach (var child in PlatformView.Subviews)
            child.RemoveFromSuperview();
#endif
    }

#if ANDROID
    private sealed class NativeViewContainer(Android.Content.Context context) : Android.Widget.FrameLayout(context)
    {
        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        {
            base.OnLayout(changed, left, top, right, bottom);
            // MAUI can measure with AT_MOST, then arrange a wider host. Fill the final bounds.
            var child = GetChildAt(0);
            if (child is null)
                return;
            var width = right - left;
            var height = bottom - top;
            child.Measure(
                MeasureSpec.MakeMeasureSpec(width, Android.Views.MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec(height, Android.Views.MeasureSpecMode.Exactly)
            );
            child.Layout(0, 0, width, height);
        }
    }
#endif

    private static async void Observe(Task cleanup)
    {
        try
        {
            await cleanup.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Native view cleanup failed: {0}", error);
        }
    }
}
