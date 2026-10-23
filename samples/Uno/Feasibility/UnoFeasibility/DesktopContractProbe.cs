using NativeChannels;

namespace UnoFeasibility;

// Runs against packaged Swift sample adapters, with no project references or UI dependencies.
internal static class DesktopContractProbe
{
    internal static async Task RunAsync()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            Console.Error.WriteLine($"Desktop contract session {iteration + 1}: starting");
            await using var service = new ChannelService(
                new BinaryMessenger(new NativeChannelTransport([new("", "MCSampleModule", "NativeChannelsSample")]))
            );
            using var methods = service.GetMethodChannel("sample");
            methods.SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>("token"));
            Require(await methods.InvokeAsync<string>("callback") == "native:token", "native callback");
            Require(await methods.InvokeAsync<long>("echo", long.MaxValue) == long.MaxValue, "Int64 envelope");
            try
            {
                await methods.InvokeAsync("fail");
                throw new InvalidOperationException("Expected native error");
            }
            catch (ChannelException error) when (error.Code == "sample_error") { }
            try
            {
                await methods.InvokeAsync("missing");
                throw new InvalidOperationException("Expected missing method");
            }
            catch (MethodNotImplementedException) { }
            using var messages = service.GetBasicMessageChannel(
                "sample.messages.binary",
                new() { Codec = BinaryMessageCodec.Instance }
            );
            messages.SetMessageHandler((value, _) => ValueTask.FromResult(value));
            byte[] bytes = [0, 127, 255];
            Require((await messages.SendAsync<byte[]>(bytes))!.SequenceEqual(bytes), "binary callback");
            using var events = service.GetEventChannel("sample.events");
            var received = new List<object?>();
            await foreach (var value in events.ListenAsync<object>())
                received.Add(value);
            Require(received.Count == 2 && received[0] is null && Convert.ToInt32(received[1]) == 42, "event stream");
            using var cancellation = new CancellationTokenSource();
            var pending = methods.InvokeAsync("delay", 10000, cancellationToken: cancellation.Token);
            cancellation.Cancel();
            try
            {
                await pending;
                throw new InvalidOperationException("Expected cancellation");
            }
            catch (OperationCanceledException) { }
            Require(await methods.InvokeAsync<int>("echo", 42) == 42, "reuse after cancellation");
            var disposed = methods.InvokeAsync("delay", 10000);
            await service.DisposeAsync();
            try
            {
                await disposed;
                throw new InvalidOperationException("Expected disposal");
            }
            catch (ObjectDisposedException) { }
            await service.DisposeAsync();
        }
    }

    private static void Require(bool value, string operation)
    {
        if (!value)
            throw new InvalidOperationException("Desktop contract failed: " + operation);
        Console.Error.WriteLine("Desktop contract passed: " + operation);
    }
}
