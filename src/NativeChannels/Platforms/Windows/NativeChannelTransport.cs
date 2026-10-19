#if WINDOWS || DESKTOP
namespace NativeChannels;

public sealed partial class NativeChannelTransport
{
#if WINDOWS && !DESKTOP
    private partial void Initialize(IEnumerable<NativeModuleRegistration> modules) => InitializeWindows(modules);

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed) =>
        SendWindowsNative(request, completed);

    private partial void CancelNative(Guid id) => CancelWindowsNative(id);

    private partial Task ShutdownNativeAsync() => ShutdownWindowsNativeAsync();
#endif

    private InMemoryTransport _windowsEndpoint = null!;
    private ChannelService _windowsChannels = null!;
    private readonly List<IWindowsChannelModule> _windowsModules = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, WindowsRequest> _windowsRequests = new();

    internal IChannelService WindowsChannels => _windowsChannels;

    private void InitializeWindows(IEnumerable<NativeModuleRegistration> modules)
    {
        // Windows adapters can call native APIs directly from .NET. Separate endpoints still
        // serialize every message and preserve the same ownership and dispatch rules as mobile.
        var (managed, native) = InMemoryTransport.CreatePair();
        _windowsEndpoint = managed;
        managed.SetMessageHandler(ReceiveAsync);
        _windowsChannels = new(new BinaryMessenger(native));
        try
        {
            foreach (var registration in modules)
            {
                var factory =
                    registration.WindowsFactory
                    ?? throw new PlatformNotSupportedException(
                        "This module has no WindowsFactory. Register a Windows adapter before using it on Windows."
                    );
                var module = factory() ?? throw new InvalidOperationException("WindowsFactory returned no module.");
                _windowsModules.Add(module);
                module.Register(_windowsChannels);
            }
        }
        catch (Exception error)
        {
            try
            {
                ShutdownWindowsNativeAsync().GetAwaiter().GetResult();
            }
            catch (Exception cleanupError)
            {
                throw new ChannelTransportException(
                    "Windows module initialization and cleanup failed.",
                    new AggregateException(error, cleanupError)
                );
            }
            throw new ChannelTransportException(
                "Windows module initialization failed. Verify its WindowsFactory and registrations.",
                error
            );
        }
    }

    private IDisposable SendWindowsNative(TransportRequest request, Action<TransportResponse> completed)
    {
        var operation = new WindowsRequest();
        if (!_windowsRequests.TryAdd(request.Id, operation))
        {
            operation.Dispose();
            throw new ChannelTransportException("Duplicate Windows request ID.");
        }
        _ = SendWindowsAsync(request, operation, completed);
        return operation;
    }

    private async Task SendWindowsAsync(
        TransportRequest request,
        WindowsRequest operation,
        Action<TransportResponse> completed
    )
    {
        TransportResponse response;
        try
        {
            response = await _windowsEndpoint.SendAsync(request, operation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            response = new(TransportStatus.Cancelled, default);
        }
        catch (ObjectDisposedException)
        {
            response = new(TransportStatus.Disposed, default);
        }
        catch (Exception)
        {
            response = new(TransportStatus.HandlerError, default);
        }
        _windowsRequests.TryRemove(request.Id, out _);
        completed(response);
    }

    private void CancelWindowsNative(Guid id)
    {
        if (_windowsRequests.TryGetValue(id, out var operation))
        {
            operation.Cancel();
        }
    }

    private async Task ShutdownWindowsNativeAsync()
    {
        // Stop both directions before releasing adapter resources, including if a module throws.
        await _windowsEndpoint.DisposeAsync().ConfigureAwait(false);
        await _windowsChannels.DisposeAsync().ConfigureAwait(false);
        List<Exception> errors = [];
        for (var index = _windowsModules.Count - 1; index >= 0; index--)
        {
            try
            {
                _windowsModules[index].Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        _windowsModules.Clear();
        foreach (var pending in _pending.Values)
        {
            pending.Completion.TrySetResult(new(TransportStatus.Disposed, default));
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Windows adapter cleanup failed.", errors);
        }
    }

    private sealed class WindowsRequest : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;
        public CancellationToken Token => _cancellation.Token;

        public void Cancel()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    try
                    {
                        _cancellation.Cancel();
                    }
                    catch (AggregateException error)
                    {
                        System.Diagnostics.Trace.TraceError("Windows cancellation callback failed: {0}", error);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _cancellation.Dispose();
            }
        }
    }
}
#endif
