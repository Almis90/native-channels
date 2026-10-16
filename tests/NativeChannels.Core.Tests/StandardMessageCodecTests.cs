using System.Text.Json;
using Xunit;

namespace NativeChannels.Tests;

public sealed class StandardMessageCodecTests
{
    private static readonly StandardMessageCodec Codec = StandardMessageCodec.Instance;

    [Fact]
    public void SharedExactBytesAndMalformedFixtures()
    {
        using var fixtures = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "standard-values.json"))
        );
        foreach (var fixture in fixtures.RootElement.GetProperty("valid").EnumerateArray())
        {
            var expected = Convert.FromHexString(fixture.GetProperty("hex").GetString()!);
            Assert.Equal(expected, Codec.Encode(Value(fixture.GetProperty("value"))).ToArray());
            Assert.Equal(expected, Codec.Encode(Codec.Decode(expected)).ToArray());
            for (var length = 0; length < expected.Length; length++)
                Assert.Throws<ChannelSerializationException>(() => Codec.Decode(expected.AsMemory(0, length)));
        }
        foreach (var invalid in fixtures.RootElement.GetProperty("invalid").EnumerateArray())
            Assert.Throws<ChannelSerializationException>(() =>
                Codec.Decode(Convert.FromHexString(invalid.GetString()!))
            );
    }

    [Theory]
    [InlineData(253)]
    [InlineData(254)]
    [InlineData(65535)]
    [InlineData(65536)]
    public void ExpandingLengthsAndTypedArrayCounts(int length)
    {
        var bytes = new byte[length];
        var encoded = Codec.Encode(bytes).ToArray();
        Assert.Equal(
            length < 254 ? (byte)length
                : length <= 65535 ? (byte)254
                : (byte)255,
            encoded[1]
        );
        Assert.Equal(bytes, Assert.IsType<byte[]>(Codec.Decode(encoded)));
        var integers = Enumerable.Range(0, length).ToArray();
        Assert.Equal(integers, Assert.IsType<int[]>(Codec.Decode(Codec.Encode(integers))));
    }

    [Fact]
    public void PreservesTypedArraysAndIeeeValues()
    {
        Assert.Equal(
            new long[] { long.MinValue, long.MaxValue },
            Assert.IsType<long[]>(Codec.Decode(Codec.Encode(new long[] { long.MinValue, long.MaxValue })))
        );
        var floats = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0f };
        var actual = Assert.IsType<float[]>(Codec.Decode(Codec.Encode(floats)));
        Assert.Equal(floats.Select(BitConverter.SingleToInt32Bits), actual.Select(BitConverter.SingleToInt32Bits));
        foreach (var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0d })
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(number),
                BitConverter.DoubleToInt64Bits(Assert.IsType<double>(Codec.Decode(Codec.Encode(number))))
            );
        Assert.IsType<long>(Codec.Decode(Codec.Encode(1L)));
        Assert.IsType<int>(Codec.Decode(Codec.Encode(1)));
        Assert.IsType<List<object?>>(Codec.Decode(Codec.Encode(new List<object?> { 1, 2 })));
    }

    [Fact]
    public void LimitsCyclesUnsupportedTypesAndBufferOwnership()
    {
        var limited = new StandardMessageCodec(new() { MaximumPayloadBytes = 32, MaximumDepth = 2 });
        Assert.Throws<ChannelSerializationException>(() => limited.Encode(new byte[32]));
        Assert.Throws<ChannelSerializationException>(() => limited.Decode(new byte[33]));
        var cycle = new List<object?>();
        cycle.Add(cycle);
        Assert.Throws<ChannelSerializationException>(() => limited.Encode(cycle));
        var nested = new object?[] { new object?[] { new object?[] { null } } };
        Assert.Throws<ChannelSerializationException>(() => limited.Decode(Codec.Encode(nested)));
        foreach (var invalid in new object[] { 1m, ulong.MaxValue, new object(), "\ud800" })
            Assert.Throws<ChannelSerializationException>(() => Codec.Encode(invalid));
        var input = new byte[] { 1, 2 };
        var wire = Codec.Encode(input).ToArray();
        input[0] = 9;
        var decoded = Assert.IsType<byte[]>(Codec.Decode(wire));
        wire[2] = 8;
        Assert.Equal(new byte[] { 1, 2 }, decoded);
    }

    [Fact]
    public void NamedAndArbitraryMapKeysRemainDistinct()
    {
        var named = new Dictionary<string, object?> { ["a"] = null, ["b"] = new[] { 1, 2 } };
        var result = Assert.IsType<Dictionary<string, object?>>(Codec.Decode(Codec.Encode(named)));
        Assert.Null(result["a"]);
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(result["b"]));
        var map = new StandardMessageMap([
            new(null, true),
            new(1, "int"),
            new(1L, "long"),
            new(new byte[] { 1 }, null),
        ]);
        var arbitrary = Assert.IsType<StandardMessageMap>(Codec.Decode(Codec.Encode(map)));
        Assert.Equal(4, arbitrary.Entries.Count);
        Assert.Null(arbitrary.Entries[0].Key);
        Assert.IsType<int>(arbitrary.Entries[1].Key);
        Assert.IsType<long>(arbitrary.Entries[2].Key);
    }

    [Fact]
    public async Task BidirectionalBasicMessagesPreserveTypedResults()
    {
        var (left, right) = InMemoryTransport.CreatePair();
        await using var client = new ChannelService(new BinaryMessenger(left));
        await using var server = new ChannelService(new BinaryMessenger(right));
        using var outgoing = client.GetBasicMessageChannel("standard", new() { Codec = Codec });
        using var incoming = server.GetBasicMessageChannel("standard", new() { Codec = Codec });
        using var echo = incoming.SetMessageHandler((value, _) => ValueTask.FromResult(value));
        Assert.Equal(new[] { 1, 2 }, await outgoing.SendAsync<int[]>(new[] { 1, 2 }));
        Assert.Null(await outgoing.SendAsync(null));
    }

    private static object? Value(JsonElement value) =>
        value[0].GetString() switch
        {
            "n" => null,
            "b" => value[1].GetBoolean(),
            "s" => value[1].GetString(),
            "i32" => value[1].GetInt32(),
            "i64" => long.Parse(value[1].GetString()!),
            "f64" => value[1].GetDouble(),
            "bytes" => value[1].EnumerateArray().Select(x => x.GetByte()).ToArray(),
            "i32s" => value[1].EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            "i64s" => value[1].EnumerateArray().Select(x => long.Parse(x.GetString()!)).ToArray(),
            "f32s" => value[1].EnumerateArray().Select(x => x.GetSingle()).ToArray(),
            "f64s" => value[1].EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            "list" => value[1].EnumerateArray().Select(Value).ToList(),
            "map" => new StandardMessageMap(
                value[1]
                    .EnumerateArray()
                    .Select(x => new KeyValuePair<object?, object?>(Value(x[0]), Value(x[1])))
                    .ToList()
            ),
            _ => throw new InvalidOperationException(),
        };
}
