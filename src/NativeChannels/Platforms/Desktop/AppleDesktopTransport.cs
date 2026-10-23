#if DESKTOP
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace NativeChannels;

// Raw channel envelopes cross this ABI unchanged; the existing Swift bridge still owns dispatch.
internal sealed class AppleDesktopTransport : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, Lazy<nint>> Libraries = new();
    private readonly object _gate = new();
    private readonly Callback _callback;
    private readonly ConcurrentDictionary<Guid, Action<TransportResponse>> _pending = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _incoming = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>>? _handler;
    private nint _bridge;
    private bool _stopping;
    private Task? _shutdown;

    internal AppleDesktopTransport(IEnumerable<NativeModuleRegistration> modules)
    {
        _callback = OnCallback;
        if (Version() != 1)
            throw new ChannelTransportException("Unsupported Apple desktop ABI version.");
        _bridge = Create(_callback);
        if (_bridge == 0)
            throw new ChannelTransportException("Could not create the Apple desktop bridge.");
        try
        {
            foreach (var module in modules)
            {
                LoadLibrary(module.AppleFramework);
                if (LoadModule(_bridge, module.AppleClass) != 0)
                    throw new ChannelTransportException(
                        $"Apple desktop module could not be loaded: {module.AppleClass}"
                    );
            }
        }
        catch
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private static void LoadLibrary(string? name)
    {
        if (name is null)
            return;
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains('.'))
            throw new ArgumentException("Use a simple Apple framework name without an extension.");
        // Objective-C registers classes globally. Libraries stay loaded for process lifetime.
        _ = Libraries
            .GetOrAdd(
                name,
                static library => new Lazy<nint>(() =>
                    NativeLibrary.Load("lib" + library + ".dylib", typeof(AppleDesktopTransport).Assembly, null)
                )
            )
            .Value;
    }

    internal nint CreateView(NativeModuleRegistration factory, string identity, string channel, byte[] arguments)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            LoadLibrary(factory.AppleFramework);
            var host = CreateNativeView(_bridge, factory.AppleClass, identity, channel, arguments, arguments.Length);
            return host != 0 ? host : throw new ChannelTransportException("Apple desktop native view creation failed.");
        }
    }

    [DllImport(Library, EntryPoint = "nc_apple_view_create")]
    private static extern nint CreateNativeView(
        nint bridge,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string factory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string identity,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string channel,
        byte[] arguments,
        int count
    );

    internal void SetMessageHandler(Func<TransportRequest, CancellationToken, ValueTask<TransportResponse>> handler) =>
        _handler = handler;

    internal IDisposable Send(TransportRequest request, Action<TransportResponse> completed)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (!_pending.TryAdd(request.Id, completed))
                throw new ChannelTransportException("Duplicate request ID.");
            try
            {
                var data = request.Payload.ToArray();
                SendRequest(_bridge, request.Id.ToString("D"), request.Channel, data, data.Length);
                return new Pending(this, request.Id);
            }
            catch
            {
                _pending.TryRemove(request.Id, out _);
                throw;
            }
        }
    }

    internal void Cancel(Guid id)
    {
        lock (_gate)
            if (_bridge != 0 && !_stopping)
                CancelRequest(_bridge, id.ToString("D"));
    }

    private void OnCallback(int kind, string idText, string channel, int status, nint bytes, int length)
    {
        try
        {
            if (kind == 3)
            {
                _closed.TrySetResult();
                return;
            }
            if (!Guid.TryParse(idText, out var id))
                return;
            if (kind == 2)
            {
                if (_incoming.TryGetValue(id, out var cancellation))
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                return;
            }
            if (length < 0 || length > 16 * 1024 * 1024)
                throw new ChannelTransportException("Invalid Apple desktop payload length.");
            var data = new byte[length];
            if (length != 0)
                Marshal.Copy(bytes, data, 0, length);
            if (kind == 0)
            {
                if (_pending.TryRemove(id, out var complete))
                    complete(new((TransportStatus)status, data));
            }
            else if (kind == 1)
            {
                _ = ReceiveAsync(new(id, channel, data));
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Apple desktop callback failed: {0}", error);
        }
    }

    private async Task ReceiveAsync(TransportRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            if (_stopping)
                return;
            if (!_incoming.TryAdd(request.Id, cancellation))
                return;
        }
        TransportResponse response;
        try
        {
            response = _handler is { } handler
                ? await handler(request, cancellation.Token).ConfigureAwait(false)
                : new(TransportStatus.ChannelNotFound, default);
        }
        catch (OperationCanceledException)
        {
            response = new(TransportStatus.Cancelled, default);
        }
        catch (Exception)
        {
            response = new(TransportStatus.HandlerError, default);
        }
        finally
        {
            _incoming.TryRemove(request.Id, out _);
        }
        lock (_gate)
        {
            if (_bridge == 0 || _stopping)
                return;
            var data = response.Payload.ToArray();
            Complete(_bridge, request.Id.ToString("D"), (int)response.Status, data, data.Length);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_shutdown is not null)
                return new(_shutdown);
            _stopping = true;
            Close(_bridge);
            _shutdown = FinishCloseAsync();
            return new(_shutdown);
        }
    }

    private async Task FinishCloseAsync()
    {
        await _closed.Task.ConfigureAwait(false);
        lock (_gate)
        {
            Destroy(_bridge);
            _bridge = 0;
        }
        foreach (var cancellation in _incoming.Values)
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (AggregateException error)
            {
                System.Diagnostics.Trace.TraceError("Apple desktop cancellation failed: {0}", error);
            }
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var complete))
                complete(new(TransportStatus.Disposed, default));
        GC.KeepAlive(_callback);
    }

    private sealed class Pending(AppleDesktopTransport owner, Guid id) : IDisposable
    {
        public void Dispose() => owner._pending.TryRemove(id, out _);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Callback(
        int kind,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string channel,
        int status,
        nint payload,
        int length
    );

    private const string Library = "NativeChannelsNative";

    [DllImport(Library, EntryPoint = "nc_apple_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Version();

    [DllImport(Library, EntryPoint = "nc_apple_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Create(Callback callback);

    [DllImport(Library, EntryPoint = "nc_apple_load_module", CallingConvention = CallingConvention.Cdecl)]
    private static extern int LoadModule(nint bridge, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library, EntryPoint = "nc_apple_send", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SendRequest(
        nint bridge,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string channel,
        byte[] payload,
        int length
    );

    [DllImport(Library, EntryPoint = "nc_apple_cancel", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CancelRequest(nint bridge, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

    [DllImport(Library, EntryPoint = "nc_apple_complete", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Complete(
        nint bridge,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        int status,
        byte[] payload,
        int length
    );

    [DllImport(Library, EntryPoint = "nc_apple_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Close(nint bridge);

    [DllImport(Library, EntryPoint = "nc_apple_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(nint bridge);
}
#endif
