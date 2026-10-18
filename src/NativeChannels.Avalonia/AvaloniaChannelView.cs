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
        previous?.Release();
        Content = new Surface(this);
    }

    public Task DisconnectAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var previous = Content as Surface;
        Content = null;
        previous?.Release();
        return _cleanup;
    }

    private sealed class Surface(AvaloniaChannelView owner) : NativeControlHost
    {
        private IAvaloniaNativeView? _view;

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            Dispatcher.UIThread.VerifyAccess();
            _view = owner._create(parent, Guid.NewGuid().ToString("N"));
            owner._current = _view;
            var attached = _view;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(owner._current, attached))
                    owner.Connected?.Invoke(owner, EventArgs.Empty);
            });
            return _view.Handle;
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control) => Release();

        internal void Release()
        {
            Dispatcher.UIThread.VerifyAccess();
            var view = _view;
            _view = null;
            if (view is null)
                return;
            if (ReferenceEquals(owner._current, view))
                owner._current = null;
            view.Channel.Dispose();
            try
            {
                view.Destroy();
            }
            finally
            {
                owner._cleanup = Task.WhenAll(owner._cleanup, view.DisposeAsync().AsTask());
                _ = owner._cleanup.ContinueWith(
                    task => System.Diagnostics.Trace.TraceError(task.Exception!.ToString()),
                    TaskContinuationOptions.OnlyOnFaulted
                );
                owner.Disconnected?.Invoke(owner, EventArgs.Empty);
            }
        }
    }
}
