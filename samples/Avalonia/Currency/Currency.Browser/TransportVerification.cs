using NativeChannels;
#if UNO_SAMPLE
using AvaloniaChannelHost = NativeChannels.UnoChannelHost;
#else
using Avalonia.Threading;
#endif

namespace Currency.Browser;

internal static class TransportVerification
{
    internal static async Task<string> RunAsync(
#if UNO_SAMPLE
        Microsoft.UI.Xaml.Window window
#endif
    )
    {
        await VerifyCancellationDuringInitializationAsync();
#if UNO_SAMPLE
        var dispatcher = new UnoChannelDispatcher(window.DispatcherQueue);
#endif
        var module = new NativeModuleRegistration("", "") { BrowserModule = "./verification.mjs" };
        await using var host = AvaloniaChannelHost.Create(services =>
            services.AddNativeChannels(options => options.Modules.Add(module))
        );
        await using var session = host.CreateSession(
#if UNO_SAMPLE
            window
#endif
        );
        var channels = session.Channels;
        using var methods = channels.GetMethodChannel("verify.methods");
        Check(await methods.InvokeAsync<long>("echo", long.MaxValue) == long.MaxValue, "Int64 maximum");
        Check(await methods.InvokeAsync<long>("echo", long.MinValue) == long.MinValue, "Int64 minimum");
        var values = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => methods.InvokeAsync<int>("echo", i)));
        Check(values.SequenceEqual(Enumerable.Range(0, 20)), "Concurrent replies");
        using var missing = channels.GetMethodChannel("verify.missing");
        await ExpectAsync<ChannelNotFoundException>(() => missing.InvokeAsync("missing"));
        await ExpectAsync<MethodNotImplementedException>(() => methods.InvokeAsync("missing"));
        var error = await ExpectAsync<ChannelException>(() => methods.InvokeAsync("error", long.MaxValue));
        Check(
            error.Code == "test_error" && error.Platform == "browser" && Equals(error.Details, long.MaxValue),
            "Structured errors"
        );

        using var binary = channels.GetBasicMessageChannel("verify.bytes", new() { Codec = new BinaryMessageCodec() });
        byte[] bytes = [0, 255, 128, 1];
        Check((await binary.SendAsync<byte[]>(bytes))!.SequenceEqual(bytes), "Binary buffers");
        // A second name avoids a codec conflict while exercising the same raw byte endpoint.
        using var standardEcho = channels.GetBasicMessageChannel(
            "verify.bytes-standard",
            new() { Codec = new StandardMessageCodec() }
        );
        Check(await standardEcho.SendAsync<long>(long.MaxValue) == long.MaxValue, "Standard binary codec");

        using var managed = channels.GetMethodChannel(
            "verify.managed",
            new()
            {
                HandlerDispatcher =
#if UNO_SAMPLE
                dispatcher.DispatchAsync
#else
                AvaloniaChannelDispatchers.UIThread
#endif
            }
        );
        using var registration = managed.SetMethodCallHandler(
            async (call, token) =>
            {
#if UNO_SAMPLE
                Check(dispatcher.HasThreadAccess, "Uno UI callback dispatch");
#else
                Check(Dispatcher.UIThread.CheckAccess(), "Avalonia UI callback dispatch");
#endif
                return await methods.InvokeAsync<long>("echo", call.Arguments, cancellationToken: token);
            }
        );
        using var callback = channels.GetMethodChannel("verify.callback");
        Check(await callback.InvokeAsync<long>("echo", long.MaxValue) == long.MaxValue, "JS to managed callback");

        using var cancellation = new CancellationTokenSource();
        var cancelled = methods.InvokeAsync("slow", cancellationToken: cancellation.Token);
        await Task.Delay(50);
        cancellation.Cancel();
        await ExpectAsync<OperationCanceledException>(() => cancelled);
        await ExpectAsync<TimeoutException>(() =>
            methods.InvokeAsync("slow", options: new() { Timeout = TimeSpan.FromMilliseconds(50) })
        );
        Check(await methods.InvokeAsync<int>("cancellations") == 2, "Cancellation reached JS handlers");

        var managedCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var managedSlow = channels.GetMethodChannel("verify.managed-slow");
        using var slowRegistration = managedSlow.SetMethodCallHandler(
            async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    managedCancelled.TrySetResult();
                    throw;
                }
                return null;
            }
        );
        using var cancelManaged = channels.GetMethodChannel("verify.cancel-managed");
        Check(
            await cancelManaged.InvokeAsync<int>("cancel") == (int)TransportStatus.Cancelled,
            "JS cancelled managed request"
        );
        await managedCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var isolated = new ChannelService(new BinaryMessenger(new NativeChannelTransport()));
        using var isolatedMethod = isolated.GetMethodChannel("verify.methods");
        await ExpectAsync<ChannelNotFoundException>(() => isolatedMethod.InvokeAsync("echo", 1));
        var pending = methods.InvokeAsync("slow");
        await Task.Delay(10);
        await session.DisposeAsync();
        await session.DisposeAsync();
        Check(session.Stopping.IsCancellationRequested, "Host session stopping token");
        await ExpectAsync<ObjectDisposedException>(() => pending);
        await ExpectAsync<ObjectDisposedException>(() => methods.InvokeAsync("echo", 1));
        await using var restarted = host.CreateSession(
#if UNO_SAMPLE
            window
#endif
        );
        using var restartedMethod = restarted.Channels.GetMethodChannel("verify.methods");
        Check(await restartedMethod.InvokeAsync<int>("echo", 42) == 42, "Fresh session after disposal");

        await using var broken = new ChannelService(
            new BinaryMessenger(
                new NativeChannelTransport([
                    new NativeModuleRegistration("", "") { BrowserModule = "./verification-invalid.mjs" },
                ])
            )
        );
        using var brokenMethod = broken.GetMethodChannel("missing");
        await ExpectAsync<ChannelTransportException>(() => brokenMethod.InvokeAsync("echo"));
        return "PASS browser transport: Int64, binary, concurrency, Avalonia UI callbacks, errors, cancellation, timeout, isolation, host session disposal/restart, initialization failure";
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException("Failed browser scenario: " + scenario);
    }

    private static async Task VerifyCancellationDuringInitializationAsync()
    {
        await using var transport = new NativeChannelTransport([
            new NativeModuleRegistration("", "") { BrowserModule = "./verification-delayed.mjs" },
        ]);
        var id = Guid.NewGuid();
        var codec = new JsonMethodCodec();
        using var cancellation = new CancellationTokenSource();
        var first = transport.SendAsync(
            new(id, "verify.methods", codec.EncodeCall(new("echo", 1))),
            cancellation.Token
        );
        cancellation.Cancel();
        Check((await first).Status == TransportStatus.Cancelled, "Cancel during initialization");
        var next = await transport
            .SendAsync(new(id, "verify.methods", codec.EncodeCall(new("echo", 2))))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Check(
            next.Status == TransportStatus.Success && Equals(codec.DecodeResult(next.Payload), 2),
            "Reused ID after cancellation during initialization"
        );
    }

    private static async Task<T> ExpectAsync<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T error)
        {
            return error;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
