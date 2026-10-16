#if ANDROID
using NativeChannels.Native.Android;

namespace NativeChannels;

public sealed partial class NativeChannelTransport
{
    private NativeBridge _bridge = null!;
    private Receiver _receiver = null!;

    private partial void Initialize(IEnumerable<NativeModuleRegistration> modules)
    {
        _bridge = new();
        if (_bridge.ProtocolVersion != 1)
        {
            throw new ChannelTransportException("Unsupported native bridge version.");
        }

        _receiver = new(this);
        _bridge.SetRequestReceiver(_receiver);
        try
        {
            foreach (var module in modules)
            {
                _bridge.LoadModule(module.AndroidClass);
            }
        }
        catch (Exception exception)
        {
            _bridge.Close();
            _receiver.Dispose();
            _bridge.Dispose();
            throw new ChannelTransportException(
                "Android module initialization failed. Verify its AAR and registration class.",
                exception
            );
        }
    }

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed)
    {
        var peer = new Completion(completed);
        try
        {
            _bridge.SendRequest(request.Id.ToString("D"), request.Channel, request.Payload.ToArray(), peer);
            return peer;
        }
        catch
        {
            peer.Dispose();
            throw;
        }
    }

    private partial void CancelNative(Guid id) => _bridge.Cancel(id.ToString("D"));

    private async partial Task ShutdownNativeAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var peer = new Completion(_ => completion.TrySetResult());
        _bridge.CloseAsync(peer);
        await completion.Task.ConfigureAwait(false);
        _receiver.Dispose();
        _bridge.Dispose();
    }

    private sealed class Completion(Action<TransportResponse> completed) : Java.Lang.Object, IReply
    {
        public void Complete(int status, byte[] payload) => completed(new((TransportStatus)status, payload.ToArray()));
    }

    private sealed class Receiver(NativeChannelTransport owner) : Java.Lang.Object, IRequestHandler
    {
        public void OnRequest(string channel, byte[] payload, IRequestContext context)
        {
            if (!Guid.TryParse(context.RequestId, out var id))
            {
                context.Complete(2, []);
                return;
            }
            _ = HandleAsync(new(id, channel, payload.ToArray()), context);
        }

        private async Task HandleAsync(TransportRequest request, IRequestContext context)
        {
            using var cancellation = new CancellationTokenSource();
            var relay = new CancelRelay(cancellation);
            try
            {
                context.OnCancelled(relay);
                var response = await owner.ReceiveAsync(request, cancellation.Token).ConfigureAwait(false);
                context.Complete((int)response.Status, response.Payload.ToArray());
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Native incoming completion failed: {0}", exception);
            }
            finally
            {
                GC.KeepAlive(relay);
                GC.KeepAlive(context);
            }
        }
    }

    private sealed class CancelRelay(CancellationTokenSource cancellation) : Java.Lang.Object, ICancellationHandler
    {
        public void OnCancel()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Cancellation callback failed: {0}", exception);
            }
        }
    }
}
#endif
