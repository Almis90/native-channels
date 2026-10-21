using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NativeChannels;

/// <summary>An adapter-owned native element or documented Uno native wrapper and its isolated channel.</summary>
public interface IUnoNativeView : IAsyncDisposable
{
    object NativeElement { get; }
    MethodChannel Channel { get; }
    string InstanceId { get; }

    /// <summary>Release native callbacks and resources synchronously after detachment, on the owning UI queue.</summary>
    void Destroy();
}

/// <summary>Experimental native-view lifecycle surface. Requires a backend with public Uno embedding support.</summary>
public sealed class UnoChannelView : ContentControl, IAsyncDisposable
{
    private readonly Func<string, IUnoNativeView> _create;
    private readonly ContentPresenter _presenter = new();
    private IUnoNativeView? _current;
    private Task _cleanup = Task.CompletedTask;
    private bool _disabled;
    private bool _disposed;
    private nint _preparedWindow;
    public MethodChannel? Channel => _current?.Channel;
    public string? InstanceId => _current?.InstanceId;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public UnoChannelView(Func<string, IUnoNativeView> create)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        Content = _presenter;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Release();
        LayoutUpdated += (_, _) => PrepareWindowsParent();
    }

    private void VerifyAccess() => new UnoChannelDispatcher(DispatcherQueue).VerifyAccess();

    private void Attach()
    {
        VerifyAccess();
        if (_disabled || _disposed || _current is not null || !IsLoaded)
            return;
        if (OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "Uno 6.7.135 has no public macOS native-element hosting API. AppKit embedding is not supported by UnoChannelView."
            );
        var view = _create(Guid.NewGuid().ToString("N"));
        _current = view;
        try
        {
            _presenter.Content = view.NativeElement;
            PrepareWindowsParent();
        }
        catch
        {
            Release();
            throw;
        }
        // Connected means the adapter is assigned; it does not certify native parenting/input.
        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareWindowsParent()
    {
        if (
            !OperatingSystem.IsWindows()
            || _current?.NativeElement is not global::Uno.UI.NativeElementHosting.Win32NativeWindow window
            || _preparedWindow == window.Hwnd
        )
            return;
        if (WindowsNativeViewClipping.Prepare(window.Hwnd))
            _preparedWindow = window.Hwnd;
    }

    public void Reconnect()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Release();
        _disabled = false;
        Attach();
    }

    public Task DisconnectAsync()
    {
        VerifyAccess();
        _disabled = true;
        Release();
        return _cleanup;
    }

    private void Release()
    {
        VerifyAccess();
        var view = _current;
        _current = null;
        _preparedWindow = 0;
        if (view is null)
            return;
        var errors = new List<Exception>();
        try
        {
            _presenter.Content = null;
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            view.Channel.Dispose();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            view.Destroy();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        Task disposal;
        try
        {
            disposal = view.DisposeAsync().AsTask();
        }
        catch (Exception error)
        {
            disposal = Task.FromException(error);
        }
        _cleanup = Task.WhenAll(
            _cleanup,
            disposal,
            errors.Count == 0 ? Task.CompletedTask : Task.FromException(new AggregateException(errors))
        );
        _ = _cleanup.ContinueWith(
            task => System.Diagnostics.Trace.TraceError(task.Exception!.ToString()),
            TaskContinuationOptions.OnlyOnFaulted
        );
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Call on the UI thread and await before closing the window.</summary>
    public ValueTask DisposeAsync()
    {
        VerifyAccess();
        _disposed = true;
        return new(DisconnectAsync());
    }
}
