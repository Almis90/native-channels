using System.Text;
using NativeChannels.Native.Android;

namespace NativeChannels.PackageConsumer;

internal static class BridgeProbe
{
    public static async Task<string> RunAsync()
    {
        using var bridge = new NativeBridge();
        using var receiver = new Receiver();
        using var reply = new Completion();
        bridge.SetReceiver(receiver);
        bridge.LoadModule("dev.nativechannels.sample.ProbeModule");
        try
        {
            bridge.Send("probe", Encoding.UTF8.GetBytes("hello"), reply);
            return await reply.Task;
        }
        finally
        {
            bridge.SetReceiver(null);
            bridge.Close();
        }
    }

    private sealed class Receiver : Java.Lang.Object, IMessageHandler
    {
        public void OnMessage(string channel, byte[] payload, IReply reply) =>
            reply.Complete(0, Encoding.UTF8.GetBytes("managed:" + Encoding.UTF8.GetString(payload)));
    }

    private sealed class Completion : Java.Lang.Object, IReply
    {
        private readonly TaskCompletionSource<string> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public Task<string> Task => _completion.Task;

        public void Complete(int status, byte[] payload)
        {
            if (status == 0)
            {
                _completion.TrySetResult(Encoding.UTF8.GetString(payload));
            }
            else
            {
                _completion.TrySetException(new InvalidOperationException($"Native status {status}"));
            }
        }
    }
}
