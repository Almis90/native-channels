#if LINUX
namespace NativeChannels;

public sealed partial class NativeChannelTransport
{
    private InMemoryTransport _linuxEndpoint = null!;
    private ChannelService _linuxChannels = null!;
    private readonly List<ILinuxChannelModule> _linuxModules = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, LinuxRequest> _linuxRequests = new();

    internal IChannelService LinuxChannels => _linuxChannels;

    private partial void Initialize(IEnumerable<NativeModuleRegistration> modules)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The Linux transport requires Linux.");
        // The owned adapter endpoint forwards requests to native modules through LinuxNativeModule.
        // Separate endpoints preserve the same serialization, ownership and dispatch rules as mobile.
        var (managed, native) = InMemoryTransport.CreatePair();
        _linuxEndpoint = managed;
        managed.SetMessageHandler(ReceiveAsync);
        _linuxChannels = new(new BinaryMessenger(native));
        try
        {
            foreach (var registration in modules)
            {
                var factory =
                    registration.LinuxFactory
                    ?? throw new PlatformNotSupportedException(
                        "This module has no LinuxFactory. Register a Linux adapter before using it on Linux."
                    );
                var module = factory() ?? throw new InvalidOperationException("LinuxFactory returned no module.");
                _linuxModules.Add(module);
                module.Register(_linuxChannels);
            }
        }
        catch (Exception error)
        {
            try
            {
                ShutdownNativeAsync().GetAwaiter().GetResult();
            }
            catch (Exception cleanupError)
            {
                throw new ChannelTransportException(
                    "Linux module initialization and cleanup failed.",
                    new AggregateException(error, cleanupError)
                );
            }
            throw new ChannelTransportException(
                "Linux module initialization failed. Verify its LinuxFactory and registrations.",
                error
            );
        }
    }

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed)
    {
        var operation = new LinuxRequest();
        if (!_linuxRequests.TryAdd(request.Id, operation))
        {
            operation.Dispose();
            throw new ChannelTransportException("Duplicate Linux request ID.");
        }
        _ = SendLinuxAsync(request, operation, completed);
        return operation;
    }

    private async Task SendLinuxAsync(
        TransportRequest request,
        LinuxRequest operation,
        Action<TransportResponse> completed
    )
    {
        TransportResponse response;
        try
        {
            response = await _linuxEndpoint.SendAsync(request, operation.Token).ConfigureAwait(false);
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
        _linuxRequests.TryRemove(request.Id, out _);
        completed(response);
    }

    private partial void CancelNative(Guid id)
    {
        if (_linuxRequests.TryGetValue(id, out var operation))
        {
            operation.Cancel();
        }
    }

    private async partial Task ShutdownNativeAsync()
    {
        // Stop both directions before releasing adapter resources, including if a module throws.
        await _linuxEndpoint.DisposeAsync().ConfigureAwait(false);
        await _linuxChannels.DisposeAsync().ConfigureAwait(false);
        List<Exception> errors = [];
        for (var index = _linuxModules.Count - 1; index >= 0; index--)
        {
            try
            {
                _linuxModules[index].Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        _linuxModules.Clear();
        foreach (var pending in _pending.Values)
        {
            pending.Completion.TrySetResult(new(TransportStatus.Disposed, default));
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Linux adapter cleanup failed.", errors);
        }
    }

    private sealed class LinuxRequest : IDisposable
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
                        System.Diagnostics.Trace.TraceError("Linux cancellation callback failed: {0}", error);
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
