#if IOS || MACCATALYST || MACOS
using Foundation;
using NativeChannels.Native.Apple;

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
                LoadFramework(module.AppleFramework);
                var error = _bridge.LoadModule(module.AppleClass);
                if (error is not null)
                {
                    throw new ChannelTransportException(error);
                }
            }
        }
        catch
        {
            _bridge.Close();
            _receiver.Dispose();
            _bridge.Dispose();
            throw;
        }
    }

    private static void LoadFramework(string? frameworkName)
    {
        if (frameworkName is { } framework)
        {
            if (Path.GetFileName(framework) != framework || framework.Contains('.'))
            {
                throw new ArgumentException("Use a simple framework name without an extension.");
            }
            var path = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, framework + ".framework", framework);
            if (ObjCRuntime.Dlfcn.dlopen(path, 2) == IntPtr.Zero)
            {
                throw new ChannelTransportException(
                    $"Cannot load native framework '{framework}' from the application bundle."
                );
            }
        }
    }

    private partial IDisposable SendNative(TransportRequest request, Action<TransportResponse> completed)
    {
        var peer = new Completion(completed);
        try
        {
            using var data = NSData.FromArray(request.Payload.ToArray());
            _bridge.SendRequest(request.Id.ToString("D"), request.Channel, data, peer);
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

    private sealed class Completion(Action<TransportResponse> completed) : Reply
    {
        public override void Complete(int status, NSData payload) =>
            completed(new((TransportStatus)status, payload.ToArray()));
    }

    private sealed class Receiver(NativeChannelTransport owner) : RequestHandler
    {
        public override void ReceiveRequest(string channel, NSData payload, IRequestContext context)
        {
            if (!Guid.TryParse(context.RequestId, out var id))
            {
                using var empty = NSData.FromArray([]);
                context.Complete(2, empty);
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
                using var data = NSData.FromArray(response.Payload.ToArray());
                context.Complete((int)response.Status, data);
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

    private sealed class CancelRelay(CancellationTokenSource cancellation) : CancellationHandler
    {
        public override void OnCancel()
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
