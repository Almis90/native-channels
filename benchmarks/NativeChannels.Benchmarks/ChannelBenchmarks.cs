using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace NativeChannels.Benchmarks;

[MemoryDiagnoser]
public class ChannelBenchmarks
{
    private ChannelService _client = null!;
    private ChannelService _server = null!;
    private BinaryMessenger _messenger = null!;
    private MethodChannel _method = null!;
    private BasicMessageChannel _binary = null!;
    private EventChannel _events = null!;
    private readonly byte[] _payload = new byte[4096];

    [Params(false, true)]
    public bool InfiniteTimeout { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        _messenger = new BinaryMessenger(a);
        var timeout = InfiniteTimeout ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30);
        _client = new(_messenger, timeout: timeout);
        _server = new(new BinaryMessenger(b), timeout: timeout);
        _method = _client.GetMethodChannel("benchmark.method");
        _method.SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));
        var remote = _server.GetMethodChannel("benchmark.method");
        remote.SetMethodCallHandler(
            async (call, token) =>
                call.Method == "callback"
                    ? await remote.InvokeAsync<int>("echo", call.Arguments, token)
                    : call.Arguments
        );
        var options = new BasicMessageChannelOptions { Codec = BinaryMessageCodec.Instance };
        _binary = _client.GetBasicMessageChannel("benchmark.binary", options);
        _server
            .GetBasicMessageChannel("benchmark.binary", options)
            .SetMessageHandler((value, _) => ValueTask.FromResult(value));
        _events = _client.GetEventChannel("benchmark.events", new() { BufferCapacity = 64 });
        _server.GetEventChannel("benchmark.events").SetStreamHandler((_, token) => Produce(token));
    }

    [Benchmark]
    public Task<int> MethodRoundTrip() => _method.InvokeAsync<int>("echo", 42);

    [Benchmark]
    public Task<int> ReentrantRoundTrip() => _method.InvokeAsync<int>("callback", 42);

    [Benchmark]
    public Task<byte[]?> BinaryRoundTrip() => _binary.SendAsync<byte[]>(_payload);

    [Benchmark(OperationsPerInvoke = 64)]
    public async Task<int> EventDelivery()
    {
        var count = 0;
        await foreach (var value in _events.ListenAsync<int>())
            count += value;
        return count;
    }

    private static async IAsyncEnumerable<object?> Produce([EnumeratorCancellation] CancellationToken token)
    {
        await Task.CompletedTask;
        for (var value = 0; value < 64; value++)
        {
            token.ThrowIfCancellationRequested();
            yield return value;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        var pending = _messenger.PendingRequestCount;
        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (pending != 0)
            throw new InvalidOperationException("Benchmark left pending channel requests.");
    }
}
