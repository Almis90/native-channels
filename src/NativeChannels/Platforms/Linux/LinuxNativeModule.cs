#if LINUX
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NativeChannels;

/// <summary>Hosts a language-neutral native_channels.h module. Contains no plugin or system-API logic.</summary>
public sealed class LinuxNativeModule : ILinuxChannelModule
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<object?>> _pending = new();
    private readonly List<IDisposable> _channels = [];
    private readonly string[] _methods;
    private readonly string[] _binary;
    private readonly Reply _reply;
    private readonly Request _request;
    private readonly Cancel _cancel;
    private readonly Destroy _destroy;
    private nint _library;
    private nint _module;
    private ulong _next;

    public LinuxNativeModule(string library, Assembly owner, string[] methodChannels, string[]? binaryChannels = null)
    {
        _methods = methodChannels;
        _binary = binaryChannels ?? [];
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
            var channel = channels.GetMethodChannel(name);
            _channels.Add(channel);
            channel.SetMethodCallHandler((call, token) => Send(name, call.Method, call.Arguments, token));
        }
        foreach (var name in _binary)
        {
            var channel = channels.GetBasicMessageChannel(name, new() { Codec = BinaryMessageCodec.Instance });
            _channels.Add(channel);
            channel.SetMessageHandler((value, token) => Send(name, "", value, token));
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
            NativeLibrary.Free(_library);
            _library = 0;
        }
        foreach (var channel in _channels)
            channel.Dispose();
        _channels.Clear();
        GC.KeepAlive(_reply);
    }

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
