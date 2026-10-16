#if WINDOWS
using System.Runtime.InteropServices;

namespace NativeChannels.InteropSample;

/// <summary>Example adapter for the C ABI shared by the C++ and Rust probe DLLs.</summary>
public sealed class NativeDllModule : IWindowsChannelModule
{
    private readonly object _gate = new();
    private readonly string _channelName;
    private nint _library;
    private readonly UInt32Function _language;
    private readonly UInt32Function _processId;
    private readonly AddFunction _add;
    private readonly TransformFunction _transform;
    private MethodChannel? _methods;
    private BasicMessageChannel? _messages;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint UInt32Function();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AddFunction(long a, long b, out long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int TransformFunction(byte* input, uint length, byte* output, uint capacity);

    public NativeDllModule(string absoluteLibraryPath, string channelName)
    {
        if (!Path.IsPathFullyQualified(absoluteLibraryPath))
        {
            throw new ArgumentException("Use an absolute path to the trusted native DLL.", nameof(absoluteLibraryPath));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        _channelName = channelName;
        _library = NativeLibrary.Load(absoluteLibraryPath);
        try
        {
            if (Export<UInt32Function>("nc_abi_version")() != 1)
            {
                throw new NotSupportedException("Expected native probe ABI version 1.");
            }
            _language = Export<UInt32Function>("nc_language");
            _processId = Export<UInt32Function>("nc_process_id");
            _add = Export<AddFunction>("nc_add");
            _transform = Export<TransformFunction>("nc_transform");
        }
        catch
        {
            NativeLibrary.Free(_library);
            _library = 0;
            throw;
        }
    }

    private T Export<T>(string name)
        where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Register(IChannelService channels)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_library == 0, this);
            if (_methods is not null)
            {
                throw new InvalidOperationException("Create a fresh module for each transport.");
            }
            _methods = channels.GetMethodChannel(_channelName);
            _methods.SetMethodCallHandler(HandleMethod);
            _messages = channels.GetBasicMessageChannel(
                _channelName + ".bytes",
                new() { Codec = BinaryMessageCodec.Instance }
            );
            _messages.SetMessageHandler(HandleBytes);
        }
    }

    private ValueTask<object?> HandleMethod(MethodCall call, CancellationToken cancellation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_library == 0, this);
            cancellation.ThrowIfCancellationRequested();
            switch (call.Method)
            {
                case "language":
                    return ValueTask.FromResult<object?>((int)_language());
                case "processId":
                    return ValueTask.FromResult<object?>((long)_processId());
                case "add":
                    CheckStatus(_add(call.GetArgument<long>("a"), call.GetArgument<long>("b"), out var result));
                    return ValueTask.FromResult<object?>(result);
                default:
                    throw new MethodNotImplementedException(call.Method);
            }
        }
    }

    private unsafe ValueTask<object?> HandleBytes(object? value, CancellationToken cancellation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_library == 0, this);
            cancellation.ThrowIfCancellationRequested();
            if (value is not byte[] input)
            {
                throw new ChannelException("invalid_arguments", "Expected a non-null byte array.");
            }
            var output = new byte[input.Length];
            fixed (byte* source = input)
            fixed (byte* destination = output)
            {
                CheckStatus(_transform(source, (uint)input.Length, destination, (uint)output.Length));
            }
            return ValueTask.FromResult<object?>(output);
        }
    }

    private static void CheckStatus(int status)
    {
        if (status != 0)
        {
            throw new ChannelException(
                status == 2 ? "native_overflow" : "native_error",
                $"Native operation returned status {status}.",
                status,
                "windows"
            );
        }
    }

    public void Dispose()
    {
        // Synchronous native calls finish under the same lock before the DLL is unloaded.
        lock (_gate)
        {
            if (_library == 0)
            {
                return;
            }
            _messages?.Dispose();
            _methods?.Dispose();
            NativeLibrary.Free(_library);
            _library = 0;
        }
    }
}
#endif
