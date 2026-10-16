using System.Text;
using Xunit;

namespace NativeChannels.Tests;

public sealed class CodecTests
{
    [Fact]
    public async Task EncodedJsonBuffersRemainOwnedAcrossConcurrentCalls()
    {
        var codec = new JsonMessageCodec();
        var first = codec.Encode(new byte[] { 1, 2, 3 });
        var snapshot = first.ToArray();
        await Task.WhenAll(
            Enumerable
                .Range(0, 100)
                .Select(index =>
                    Task.Run(() =>
                    {
                        var value = new byte[4096 + index];
                        value[0] = (byte)index;
                        var encoded = codec.Encode(value);
                        Assert.Equal(value, Assert.IsType<byte[]>(codec.Decode(encoded)));
                    })
                )
        );
        Assert.Equal(snapshot, first.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(codec.Decode(first)));
    }

    [Fact]
    public void SharedProtocolFixtures()
    {
        using var fixtures = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values.json"))
        );
        var codec = new JsonMessageCodec();
        foreach (var value in fixtures.RootElement.GetProperty("valid").EnumerateArray())
        {
            var decoded = codec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
            Assert.Equal(codec.Encode(decoded).ToArray(), codec.Encode(codec.Decode(codec.Encode(decoded))).ToArray());
        }
        foreach (var value in fixtures.RootElement.GetProperty("invalid").EnumerateArray())
        {
            Assert.Throws<ChannelSerializationException>(() =>
                codec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()))
            );
        }
    }

    [Fact]
    public void PreservesPortableValueTypes()
    {
        var codec = new JsonMessageCodec();
        object?[] values =
        [
            null,
            true,
            false,
            "hello 🌍",
            int.MinValue,
            int.MaxValue,
            long.MinValue,
            long.MaxValue,
            1.25d,
            new byte[] { 0, 255, 42 },
        ];
        foreach (var value in values)
        {
            var decoded = codec.Decode(codec.Encode(value));
            if (value is byte[] bytes)
            {
                Assert.Equal(bytes, Assert.IsType<byte[]>(decoded));
            }
            else
            {
                Assert.Equal(value, decoded);
                Assert.Equal(value?.GetType(), decoded?.GetType());
            }
        }
        var nested = new Dictionary<string, object?>
        {
            ["list"] = new List<object?> { long.MaxValue, new byte[] { 1 } },
        };
        Assert.Equal(codec.Encode(nested).ToArray(), codec.Encode(codec.Decode(codec.Encode(nested))).ToArray());
    }

    [Theory]
    [InlineData("[\"i32\",2147483648]")]
    [InlineData("[\"i64\",\"9223372036854775808\"]")]
    [InlineData("[\"bytes\",\"!!!\"]")]
    [InlineData("[\"n\",0]")]
    [InlineData("[\"s\",null]")]
    [InlineData("[\"unknown\",0]")]
    [InlineData("[\"map\",{\"x\":[\"n\"],\"x\":[\"n\"]}]")]
    [InlineData("{")]
    public void RejectsMalformedValues(string json) =>
        Assert.Throws<ChannelSerializationException>(() => new JsonMessageCodec().Decode(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void RejectsOversizedDeepAndUnsupportedPayloads()
    {
        var codec = new JsonMessageCodec(limits: new() { MaximumPayloadBytes = 32, MaximumDepth = 2 });
        Assert.Throws<ChannelSerializationException>(() => codec.Encode(new string('a', 100)));
        Assert.Throws<ChannelSerializationException>(() => codec.Decode(new byte[33]));
        Assert.Throws<ChannelSerializationException>(() =>
            codec.Encode(new object[] { new object[] { new object[] { "deep" } } })
        );
        Assert.Throws<ChannelSerializationException>(() => codec.Encode(double.NaN));
        Assert.Throws<ChannelSerializationException>(() => codec.Encode(double.PositiveInfinity));
        using var oversizedInteger = System.Text.Json.JsonDocument.Parse("18446744073709551615");
        Assert.Throws<ChannelSerializationException>(() => new JsonMessageCodec().Encode(oversizedInteger.RootElement));
        var cyclic = new List<object?>();
        cyclic.Add(cyclic);
        Assert.Throws<ChannelSerializationException>(() => codec.Encode(cyclic));
    }

    [Theory]
    [InlineData("{\"v\":2,\"method\":\"x\",\"arguments\":[\"n\"]}")]
    [InlineData("{\"v\":1,\"v\":1,\"method\":\"x\",\"arguments\":[\"n\"]}")]
    [InlineData("{\"v\":1,\"method\":\"\",\"arguments\":[\"n\"]}")]
    public void RejectsInvalidMethodEnvelopes(string json) =>
        Assert.Throws<ChannelSerializationException>(() =>
            new JsonMethodCodec().DecodeCall(Encoding.UTF8.GetBytes(json))
        );
}
