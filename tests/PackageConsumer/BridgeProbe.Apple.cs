#if IOS || MACCATALYST
using System.Text;
using Foundation;
using NativeChannels.Native.Apple;

namespace NativeChannels.PackageConsumer;

internal static class BridgeProbe
{
    public static async Task<string> RunAsync()
    {
        // Loading is explicit: a module name alone does not force a dynamic framework into the process.
        var path = Path.Combine(
            NSBundle.MainBundle.PrivateFrameworksPath!,
            "NativeChannelsSample.framework/NativeChannelsSample"
        );
        var handle = ObjCRuntime.Dlfcn.dlopen(path, 2);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot load probe framework: " + path);
        }

        using var bridge = new NativeBridge();
        using var receiver = new Receiver();
        using var reply = new Completion();
        bridge.SetReceiver(receiver);
        var error = bridge.LoadModule("MCProbeModule");
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        try
        {
            using var data = NSData.FromArray(Encoding.UTF8.GetBytes("hello"));
            bridge.Send("probe", data, reply);
            return await reply.Task;
        }
        finally
        {
            bridge.SetReceiver(null);
            bridge.Close();
        }
    }

    private sealed class Receiver : MessageHandler
    {
        public override void Receive(string channel, NSData payload, IReply reply)
        {
            using var result = NSData.FromArray(
                Encoding.UTF8.GetBytes("managed:" + Encoding.UTF8.GetString(payload.ToArray()))
            );
            reply.Complete(0, result);
        }
    }

    private sealed class Completion : Reply
    {
        private readonly TaskCompletionSource<string> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public Task<string> Task => _completion.Task;

        public override void Complete(int status, NSData payload)
        {
            if (status == 0)
            {
                _completion.TrySetResult(Encoding.UTF8.GetString(payload.ToArray()));
            }
            else
            {
                _completion.TrySetException(new InvalidOperationException($"Native status {status}"));
            }
        }
    }
}
#endif
