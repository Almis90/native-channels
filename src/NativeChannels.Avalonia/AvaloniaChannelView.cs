using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NativeChannels;

/// <summary>One native control, isolated channel and owner for each attachment.</summary>
public interface IAvaloniaNativeView : IAsyncDisposable
{
    IPlatformHandle Handle { get; }
    MethodChannel Channel { get; }
    string InstanceId { get; }

    /// <summary>Detach callbacks and destroy native content synchronously on the UI thread.</summary>
    void Destroy();
}

public sealed class AvaloniaChannelView : ContentControl
{
    private readonly Func<IPlatformHandle, string, IAvaloniaNativeView> _create;
    private IAvaloniaNativeView? _current;
    private Task _cleanup = Task.CompletedTask;
    public MethodChannel? Channel => _current?.Channel;
    public string? InstanceId => _current?.InstanceId;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public AvaloniaChannelView(Func<IPlatformHandle, string, IAvaloniaNativeView> create)
    {
        _create = create;
        Content = new Surface(this);
    }

    public void Reconnect()
    {
        Dispatcher.UIThread.VerifyAccess();
        var previous = Content as Surface;
        Content = null;
        previous?.Disconnect();
        Content = new Surface(this);
    }

    public Task DisconnectAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var previous = Content as Surface;
        Content = null;
        previous?.Disconnect();
        return _cleanup;
    }

    private sealed class Surface(AvaloniaChannelView owner) : NativeControlHost
    {
        private IAvaloniaNativeView? _view;
        private TaskCompletionSource? _destroyed;
        private bool _disconnected;

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            Dispatcher.UIThread.VerifyAccess();
            _view = owner._create(parent, Guid.NewGuid().ToString("N"));
            _destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disconnected = false;
            owner._current = _view;
            var attached = _view;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(owner._current, attached))
                    owner.Connected?.Invoke(owner, EventArgs.Empty);
            });
            return _view.Handle;
        }

        internal void Disconnect()
        {
            Dispatcher.UIThread.VerifyAccess();
            var view = _view;
            if (view is null || _disconnected)
                return;
            _disconnected = true;
            owner._cleanup = Task.WhenAll(owner._cleanup, _destroyed!.Task);
            view.Channel.Dispose();
            if (ReferenceEquals(owner._current, view))
            {
                owner._current = null;
                owner.Disconnected?.Invoke(owner, EventArgs.Empty);
            }
        }

        protected override async void DestroyNativeControlCore(IPlatformHandle control)
        {
            // NativeControlHost disposes its attachment before calling this hook.
            // Destroying earlier lets its deferred cleanup act on a recycled handle
            // belonging to the replacement view (notably after Qt reconnects to X11).
            Disconnect();
            var view = _view;
            var destroyed = _destroyed;
            _view = null;
            if (view is null)
                return;
            try
            {
                try
                {
                    view.Destroy();
                }
                finally
                {
                    await view.DisposeAsync();
                }
                destroyed!.TrySetResult();
            }
            catch (Exception error)
            {
                destroyed!.TrySetException(error);
                System.Diagnostics.Trace.TraceError(error.ToString());
            }
        }
    }
}
