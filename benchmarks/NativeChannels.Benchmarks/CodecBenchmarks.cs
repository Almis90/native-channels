using BenchmarkDotNet.Attributes;

namespace NativeChannels.Benchmarks;

[MemoryDiagnoser]
public class CodecBenchmarks
{
    private readonly JsonMessageCodec _json = new();
    private readonly StandardMessageCodec _standard = new();
    private byte[] _value = null!;
    private ReadOnlyMemory<byte> _jsonPayload;
    private ReadOnlyMemory<byte> _standardPayload;
    private ReadOnlyMemory<byte> _binaryPayload;

    [Params(256, 4096, 65536)]
    public int PayloadBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _value = new byte[PayloadBytes];
        new Random(42).NextBytes(_value);
        _jsonPayload = _json.Encode(_value);
        _standardPayload = _standard.Encode(_value);
        _binaryPayload = BinaryMessageCodec.Instance.Encode(_value);
    }

    [Benchmark]
    public ReadOnlyMemory<byte> JsonEncode() => _json.Encode(_value);

    [Benchmark]
    public ReadOnlyMemory<byte> StandardEncode() => _standard.Encode(_value);

    [Benchmark]
    public ReadOnlyMemory<byte> BinaryEncode() => BinaryMessageCodec.Instance.Encode(_value);

    [Benchmark]
    public object? JsonDecode() => _json.Decode(_jsonPayload);

    [Benchmark]
    public object? StandardDecode() => _standard.Decode(_standardPayload);

    [Benchmark]
    public object? BinaryDecode() => BinaryMessageCodec.Instance.Decode(_binaryPayload);
}
