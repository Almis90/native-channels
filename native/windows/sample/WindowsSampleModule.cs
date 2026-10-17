using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;
using NativeChannels;

namespace ChannelExplorer;

internal sealed class WindowsSampleModule : IWindowsChannelModule
{
    private readonly List<IDisposable> _handles = [];
    private int _activeStreams;
    private int _nextStream;

    public void Register(IChannelService channels)
    {
        var methods = Own(channels.GetMethodChannel("sample"));
        methods.SetMethodCallHandler(
            async (call, cancellation) =>
            {
                switch (call.Method)
                {
                    case "echo":
                        return call.Arguments;
                    case "add":
                        return call.GetArgument<int>("a") + call.GetArgument<int>("b");
                    case "device":
                        return new Dictionary<string, object?>
                        {
                            ["platform"] = "Windows",
                            ["name"] = Environment.MachineName,
                            ["id"] = long.MaxValue,
                        };
                    case "null":
                        return null;
                    case "callback":
                        return "native:"
                            + await methods.InvokeAsync<string>("getToken", cancellationToken: cancellation);
                    case "delay":
                        var delay = call.GetArguments<int>();
                        await Task.Delay(delay, cancellation);
                        return delay;
                    case "fail":
                        throw new ChannelException("sample_error", "Windows adapter error", platform: "windows");
                    case "activeStreams":
                        return Volatile.Read(ref _activeStreams);
                    case "memory":
                        using (var process = System.Diagnostics.Process.GetCurrentProcess())
                        {
                            process.Refresh();
                            return new Dictionary<string, object?>
                            {
                                ["workingSet"] = process.WorkingSet64,
                                ["privateBytes"] = process.PrivateMemorySize64,
                            };
                        }
                    default:
                        throw new MethodNotImplementedException(call.Method);
                }
            }
        );
        var ui = Own(
            channels.GetMethodChannel("sample.ui", new() { HandlerDispatcher = MauiChannelDispatchers.MainThread })
        );
        ui.SetMethodCallHandler(
            (call, _) =>
                call.Method == "isMainThread"
                    ? ValueTask.FromResult<object?>(MainThread.IsMainThread)
                    : throw new MethodNotImplementedException(call.Method)
        );
        foreach (
            var (name, codec) in new (string, IMessageCodec?)[]
            {
                ("json", null),
                ("string", StringMessageCodec.Instance),
                ("binary", BinaryMessageCodec.Instance),
                ("standard", StandardMessageCodec.Instance),
            }
        )
        {
            var messages = Own(channels.GetBasicMessageChannel("sample.messages." + name, new() { Codec = codec }));
            messages.SetMessageHandler(async (value, cancellation) => await messages.SendAsync(value, cancellation));
        }
        Own(channels.GetEventChannel("sample.events")).SetStreamHandler(StreamAsync);
    }

    private async IAsyncEnumerable<object?> StreamAsync(
        object? argument,
        [EnumeratorCancellation] CancellationToken cancellation
    )
    {
        Interlocked.Increment(ref _activeStreams);
        try
        {
            switch (argument)
            {
                case "finite":
                    yield return null;
                    yield return 42;
                    break;
                case "error":
                    yield return 1;
                    throw new ChannelException("sample_stream_error", "Windows stream error", 42, "windows");
                default:
                    yield return Interlocked.Increment(ref _nextStream);
                    await Task.Delay(Timeout.Infinite, cancellation);
                    break;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeStreams);
        }
    }

    private T Own<T>(T handle)
        where T : IDisposable
    {
        _handles.Add(handle);
        return handle;
    }

    public void Dispose()
    {
        foreach (var handle in _handles)
        {
            handle.Dispose();
        }
        _handles.Clear();
    }
}
