# Codecs and typed values

The default codec uses NativeChannels' version 1 tagged JSON protocol. Type tags preserve values that
ordinary untyped JSON can blur, including Int64 and byte arrays. Request IDs, deadlines, and pending
state belong to the messenger, not the method envelope.

## Portable value types

| Value | C# | Kotlin | Swift |
| --- | --- | --- | --- |
| Null | `null` | `null` | `nil` / `NSNull` in containers |
| Boolean | `bool` | `Boolean` | `Bool` |
| Text | `string` | `String` | `String` |
| Signed 32-bit integer | `int` | `Int` | `Int32` |
| Signed 64-bit integer | `long` | `Long` | `Int64` |
| Finite floating point | `double` | `Double` | `Double` |
| Bytes | `byte[]` | `ByteArray` | `Data` |
| Sequence | List/array of supported values | `List` | Array |
| Named values | String-keyed dictionary | String-keyed `Map` | String-keyed dictionary |

Int64 values are encoded as decimal text; bytes use base64. The managed serializer also normalizes
small integer types to Int32, UInt32 to Int64, Single to Double, and DateTime/DateTimeOffset to
round-trip ISO strings. Prefer the portable types for the shared contract.

```csharp
using var channel = channels.GetMethodChannel("sample");
var bytes = await channel.InvokeAsync<byte[]>("echo", new byte[] { 0, 1, 255 });
var exact = await channel.InvokeAsync<long>("echo", long.MaxValue);
```

Non-finite floating point, decimal, UInt64, non-string map keys, cycles, and unsupported objects are
rejected. Match the result's numeric type. Checked narrowing and typed DTO/collection conversion
reject unsupported or lossy conversions; the library does not promise arbitrary numeric coercion.

## Typed DTOs

Define ordinary serializable properties and a source-generated context. The sample's native `device`
method returns a map with `platform`, `name`, and an exact signed 64-bit `id`:

```csharp
using System.Text.Json.Serialization;

public sealed record DeviceInfo(string Platform, string Name, long Id);

[JsonSerializable(typeof(DeviceInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ChannelJsonContext : JsonSerializerContext;
```

Configure the resolver before building the MAUI application:

```csharp
builder.UseNativeChannels(options =>
{
    options.Modules.Add(new NativeModuleRegistration(
        "dev.nativechannels.sample.SampleModule", "MCSampleModule", "NativeChannelsSample"));
    options.Json.TypeInfoResolver = ChannelJsonContext.Default;
});
```

Then request the DTO:

```csharp
using var channel = channels.GetMethodChannel("sample");
DeviceInfo? device = await channel.InvokeAsync<DeviceInfo>("device");
// device.Id == long.MaxValue in the bundled sample.
```

Register metadata for each DTO and collection contract you exchange. Primitive and raw map values
need no DTO context. The library does not enable a reflection fallback; missing metadata produces
`ChannelSerializationException` with the type that needs registration.

Outgoing DTOs use metadata property names and getters, preserving portable type tags inside nested
properties. Incoming DTO conversion uses System.Text.Json metadata. Keep property names consistent
with the native dictionary keys, including their casing.

## Alpha contract limits

Use ordinary property-based DTOs. Polymorphic contracts and arbitrary outgoing custom converters are
not supported: normalization reads metadata properties rather than invoking a custom converter's
wire representation. Choose explicit portable values in your adapter for those cases.

Null can be returned successfully. Assigning it to a nonnullable value type is an error. Integral
JsonElement values outside the Int64 range are rejected instead of rounded to Double. Large integer
IDs should remain Int64 end to end.

## Codec interfaces

`IMessageCodec` encodes values. `IMethodCodec` encodes method calls and success/error/not-implemented
results. `JsonMessageCodec` and `JsonMethodCodec` implement the default protocol.

Custom codecs must agree across both endpoints and share an instance across live managed handles on
the same named endpoint. Native MethodChannel helpers use the built-in format. See
[configuration](configuration.md#custom-codecs) and the [wire protocol](message-protocol.md).

## Basic message codecs

[BasicMessageChannel](basic-message-channels.md) defaults to tagged JSON values and also supports
`StringMessageCodec.Instance` and `BinaryMessageCodec.Instance`. Both distinguish null from empty;
string decoding rejects invalid UTF-8. Choose the matching Kotlin/Swift codec in your adapter.
These codecs describe value payloads, not MethodChannel envelopes or EventChannel control messages.

## StandardMessageCodec

Choose `StandardMessageCodec.Instance` for compact binary values and typed numeric arrays:

```csharp
using var channel = channels.GetBasicMessageChannel("app.standard", new()
{
    Codec = StandardMessageCodec.Instance,
});
int[]? reply = await channel.SendAsync<int[]>(new[] { 1, 2, 3 });
```

Use `StandardMessageCodec` in Kotlin and `StandardMessageCodec()` in Swift on the matching endpoint.
It is opt-in; JSON remains the default. MethodChannel and EventChannel keep their existing codecs.

| Value | C# | Kotlin | Swift |
| --- | --- | --- | --- |
| Null, Boolean, text | `null`, `bool`, `string` | `null`, `Boolean`, `String` | `nil`/`NSNull`, `Bool`, `String` |
| Signed integers | `int`, `long` | `Int`, `Long` | `Int32`, `Int64` (`Int` encodes as Int64) |
| Floating point | `double` | `Double` | `Double` |
| Byte array | `byte[]` | `ByteArray` | `Data` |
| Int32 array | `int[]` | `IntArray` | `[Int32]` |
| Int64 array | `long[]` | `LongArray` | `[Int64]` |
| Float32 array | `float[]` | `FloatArray` | `[Float]` |
| Float64 array | `double[]` | `DoubleArray` | `[Double]` |
| Heterogeneous list | `IList` / `List<object?>` | `List<*>` | `[Any]`, with `NSNull` for null entries |
| Named map | `IDictionary` with string keys | `Map` with string keys | `[String: Any]`, with `NSNull` for null values |
| Arbitrary-key map | `StandardMessageMap` | `StandardMessageMap` | `StandardMessageMap` |

Int32 and Int64 tags remain distinct even for small values. Scalar Single/Float is promoted to
Double; typed Float32 arrays preserve Float32. Unlike tagged JSON, this codec preserves IEEE 754
infinities, NaN bit patterns, and negative zero. Typed arrays stay typed even when empty. A generic
list of numbers is encoded as a list, not automatically converted to a typed array.

Decoded lists use `List<object?>`, Kotlin `List`, or Swift `[Any]`. A map with unique string keys
decodes to a string-keyed dictionary. Other maps decode to `StandardMessageMap`, an ordered collection
of key/value entries. This avoids losing null keys, collection keys, or distinct Int32/Int64 keys to
host-language dictionary equality. Repeated keys remain separate entries; dictionary ordering is
not a canonical serialization guarantee.

```csharp
var values = new StandardMessageMap(
[
    new(null, "null key"),
    new(1, "Int32 key"),
    new(1L, "Int64 key"),
]);
StandardMessageMap? reply = await channel.SendAsync<StandardMessageMap>(values);
```

Kotlin uses `StandardMessageMap(listOf(null to "null key", 1 to "Int32 key"))`.
Swift uses `StandardMessageMap([(key: nil, value: "null key"), (key: Int32(1), value: "Int32 key")])`.
Use native Swift containers; Foundation `NSArray`/`NSDictionary` instances are rejected to avoid
reference-cycle bridging. `NSNumber` Boolean/signed integer/floating representations are accepted;
`NSDecimalNumber` is rejected.

Use the explicit value types above. This codec does not normalize outgoing DTOs; send explicit maps
or use JsonMessageCodec for outgoing metadata-based DTO serialization. Typed receive conversion still
uses the channel service serializer; request the decoded types above to avoid numeric coercion.
Decimal, BigInteger, unsupported objects, malformed UTF-8, truncated/unknown tags,
nonzero alignment padding, and trailing data are rejected. The default caps are 16 MiB including tags
and padding, and depth 64. C# can impose smaller limits with `new StandardMessageCodec(limits)`;
share that instance among handles on the same endpoint. Native limits remain 16 MiB/depth 64.

The [binary format](message-protocol.md#standard-binary-values) uses standard-style value tags and
alignment inside NativeChannels frames. Top-level null is a `00` payload. This does not add Flutter
transport/plugin compatibility or StandardMethodCodec. Firestore-specific types are not supported.
