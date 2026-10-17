#if LINUX
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NativeChannels;

/// <summary>Hosts a language-neutral native_channels.h module. Contains no plugin or system-API logic.</summary>
public sealed class LinuxNativeModule : ILinuxChannelModule
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<object?>> _pending = new();
    private readonly List<IDisposable> _channels = [];
    private readonly string[] _methods;
    private readonly string[] _binary;
    private readonly string[] _events;
    private readonly ConcurrentDictionary<ulong, Channel<object?>> _streams = new();
    private readonly Reply _reply;
    private readonly Request _request;
    private readonly Cancel _cancel;
    private readonly Destroy _destroy;
    private nint _library;
    private nint _module;
    private ulong _next;
    private readonly ChannelHandlerDispatcher? _dispatcher;

    public LinuxNativeModule(
        string library,
        Assembly owner,
        string[] methodChannels,
        string[]? binaryChannels = null,
        string[]? eventChannels = null,
        ChannelHandlerDispatcher? dispatcher = null
    )
    {
        _methods = methodChannels;
        _dispatcher = dispatcher;
        _binary = binaryChannels ?? [];
        _events = eventChannels ?? [];
        _reply = Complete;
        _library = NativeLibrary.Load(library, owner, null);
        try
        {
            var create = Export<Create>("nc_create");
            _request = Export<Request>("nc_request");
            _cancel = Export<Cancel>("nc_cancel");
            _destroy = Export<Destroy>("nc_destroy");
            _module = create(_reply);
            if (_module == 0)
                throw new ChannelTransportException("Native module creation failed.");
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    private T Export<T>(string name)
        where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Register(IChannelService channels)
    {
        foreach (var name in _methods)
        {
            var channel = channels.GetMethodChannel(name, new() { HandlerDispatcher = _dispatcher });
            _channels.Add(channel);
            channel.SetMethodCallHandler((call, token) => Send(name, call.Method, call.Arguments, token));
        }
        foreach (var name in _binary)
        {
            var channel = channels.GetBasicMessageChannel(name, new() { Codec = BinaryMessageCodec.Instance });
            _channels.Add(channel);
            channel.SetMessageHandler((value, token) => Send(name, "", value, token));
        }
        foreach (var name in _events)
        {
            var channel = channels.GetEventChannel(name);
            _channels.Add(channel);
            channel.SetStreamHandler((value, token) => Stream(name, value, token));
        }
    }

    private async IAsyncEnumerable<object?> Stream(
        string name,
        object? value,
        [EnumeratorCancellation] CancellationToken token
    )
    {
        var buffer = Channel.CreateBounded<object?>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
        );
        var bytes = StandardMessageCodec.Instance.Encode(value).ToArray();
        ulong id;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_module == 0, this);
            token.ThrowIfCancellationRequested();
            id = ++_next;
            _streams[id] = buffer;
            _request(
                _module,
                id,
                name,
                "listen",
                bytes,
                (nuint)bytes.Length,
                CultureInfo.CurrentCulture.Name.Replace('-', '_')
            );
        }
        try
        {
            await foreach (var item in buffer.Reader.ReadAllAsync(token).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            lock (_gate)
                if (_streams.TryRemove(id, out _) && _module != 0)
                    _cancel(_module, id);
        }
    }

    private async ValueTask<object?> Send(string channel, string method, object? value, CancellationToken token)
    {
        if (channel.Contains('\0') || method.Contains('\0'))
            throw new ChannelException(
                "invalid_arguments",
                "Native channel and method names cannot contain NUL.",
                platform: "linux"
            );
        var bytes = StandardMessageCodec.Instance.Encode(value).ToArray();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong id;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_module == 0, this);
            token.ThrowIfCancellationRequested();
            id = ++_next;
            _pending[id] = completion;
            _request(
                _module,
                id,
                channel,
                method,
                bytes,
                (nuint)bytes.Length,
                CultureInfo.CurrentCulture.Name.Replace('-', '_')
            );
        }
        using var registration = token.Register(() =>
        {
            lock (_gate)
            {
                if (_pending.TryRemove(id, out var cancelled))
                {
                    cancelled.TrySetCanceled(token);
                    if (_module != 0)
                        _cancel(_module, id);
                }
            }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private void Complete(ulong id, int status, nint data, nuint size)
    {
        if (_streams.TryGetValue(id, out var stream))
        {
            try
            {
                var payload = new byte[checked((int)size)];
                Marshal.Copy(data, payload, 0, payload.Length);
                var value = StandardMessageCodec.Instance.Decode(payload);
                if (status == 3)
                    stream.Writer.TryWrite(value);
                else
                {
                    _streams.TryRemove(id, out _);
                    stream.Writer.TryComplete(status == 0 ? null : NativeError(value));
                }
            }
            catch (Exception error)
            {
                stream.Writer.TryComplete(error);
            }
            return;
        }
        if (!_pending.TryRemove(id, out var completion))
            return;
        try
        {
            var bytes = new byte[checked((int)size)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            var value = StandardMessageCodec.Instance.Decode(bytes);
            if (status == 2)
                completion.TrySetException(new MethodNotImplementedException("native method"));
            else if (status == 1 && value is IDictionary<string, object?> error)
                completion.TrySetException(
                    new ChannelException(
                        (string)error["code"]!,
                        (string)error["message"]!,
                        error.TryGetValue("details", out var details) ? details : null,
                        "linux"
                    )
                );
            else if (status != 0)
                completion.TrySetException(new ChannelTransportException("Malformed native reply."));
            else
                completion.TrySetResult(value);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_module == 0)
                return;
            _destroy(_module); // Joins native workers before releasing callback and library.
            _module = 0;
            foreach (var pending in _pending.Values)
                pending.TrySetException(new ObjectDisposedException(nameof(LinuxNativeModule)));
            _pending.Clear();
            foreach (var stream in _streams.Values)
                stream.Writer.TryComplete(new ObjectDisposedException(nameof(LinuxNativeModule)));
            _streams.Clear();
            NativeLibrary.Free(_library);
            _library = 0;
        }
        foreach (var channel in _channels)
            channel.Dispose();
        _channels.Clear();
        GC.KeepAlive(_reply);
    }

    private static Exception NativeError(object? value) =>
        value is IDictionary<string, object?> error
            ? new ChannelException(
                (string)error["code"]!,
                (string)error["message"]!,
                error.TryGetValue("details", out var details) ? details : null,
                "linux"
            )
            : new ChannelTransportException("Malformed native event error.");

    // Optional view extension. Toolkit ownership and all UI APIs stay in the native module.
    public nint CreateView(nint parent, string identity)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_module == 0, this);
            var handle = Export<ViewCreate>("nc_view_create")(_module, parent, identity);
            return handle != 0 ? handle : throw new ChannelTransportException("Native view creation failed.");
        }
    }

    public void PollView()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_module == 0, this);
            Export<ViewPoll>("nc_view_poll")(_module);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ViewCreate(nint module, nint parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string identity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ViewPoll(nint module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Reply(ulong id, int status, nint data, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Create(Reply reply);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Request(
        nint module,
        ulong id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string channel,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string method,
        byte[] data,
        nuint size,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string locale
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Cancel(nint module, ulong id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Destroy(nint module);
}
#endif
