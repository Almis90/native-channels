# NativeChannels JSON protocol, version 1

This protocol is specific to NativeChannels and is not Flutter's JSON or StandardMethodCodec protocol.
UTF-8 JSON contains explicitly tagged values. The native bridge advertises protocol version 1;
startup rejects another bridge version. Generic request frames carry a UUID, channel name, bytes,
and a completion callback. Correlation and cancellation belong to that bridge/messenger layer;
method envelopes do not repeat them. Buffers are copied at native boundaries.

| Type | Representation |
| --- | --- |
| Null | `["n"]` |
| Boolean | `["b",true]` |
| String | `["s","hello"]` |
| Int32 | `["i32",2147483647]` |
| Int64 | `["i64","9223372036854775807"]` |
| Finite Double | `["f64",1.25]` |
| Bytes | `["bytes","AAH/"]` |
| List | `["list",[["n"],["i32",1]]]` |
| String-keyed map | `["map",{"key":["s","value"]}]` |

Int64 uses decimal text to avoid JSON floating-point precision loss. Bytes use base64. Unknown tags,
wrong tag arity, out-of-range integers, unsupported types, and non-finite doubles are rejected.
The default limits are 16 MiB per payload, depth 64, and 256 UTF-8 bytes per channel/method name.
Names must be nonblank and cannot contain control characters. Native bridges enforce these caps;
managed limits may impose stricter application limits. Native adapters should not send duplicate JSON
object keys; JSON parser duplicate-key handling differs between Foundation, Android, and System.Text.Json.

Method call:
```json
{"v":1,"method":"add","arguments":["map",{"a":["i32",1],"b":["i32",2]}]}
```

Success (including a valid null):
```json
{"v":1,"status":"success","value":["n"]}
```

Error:
```json
{"v":1,"status":"error","code":"denied","message":"Access denied","details":["n"],"platform":"android"}
```

Unimplemented method:
```json
{"v":1,"status":"not_implemented"}
```

Transport statuses are 0 success, 1 missing channel, 2 handler/transport failure, 3 disposed, and
4 cancelled. They are separate from a method error envelope. Replies are accepted at most once.
Shared language fixtures live in `tests/fixtures/values.json`.

## Basic messages

BasicMessageChannel frames use the same transport and reply statuses. With JsonMessageCodec the
entire payload is one tagged value, for example `["i64","9223372036854775807"]`; there is no method
or result envelope. Both directions use the same codec.

StringMessageCodec and BinaryMessageCodec use a one-byte prefix:

| Bytes (hex) | Meaning |
| --- | --- |
| `00` | Null, with no following bytes permitted. |
| `01` | Empty string or empty byte array. |
| `01` followed by data | Strict UTF-8 text, or uninterpreted binary bytes. |

An absent or unknown marker, invalid UTF-8, or trailing bytes after null is an encoding error.
The 16 MiB payload cap includes the marker. These formats are specific to NativeChannels, including
the null marker, and do not implement Flutter's string or binary codecs.

## Standard binary values

StandardMessageCodec is an opt-in BasicMessageChannel value format. Multi-byte fields are explicitly
little-endian. Each value starts with one byte:

| Tag | Value and following bytes |
| --- | --- |
| 0 | Null; no additional bytes |
| 1 / 2 | True / false; no additional bytes |
| 3 / 4 | Signed Int32 / Int64, without alignment padding |
| 6 | IEEE Float64, aligned to an 8-byte offset from the start of the payload |
| 7 | UTF-8 byte count, then strict UTF-8 data |
| 8 | Byte count, then raw bytes |
| 9 / 10 | Int32 / Int64 element count, alignment to 4 / 8 bytes, then packed elements |
| 11 / 14 | Float64 / Float32 element count, alignment to 8 / 4 bytes, then packed elements |
| 12 | List count, then recursive tagged values |
| 13 | Map entry count, then recursive tagged key/value pairs |

Counts below 254 use one byte. Counts through 65535 use `fe` followed by UInt16; larger counts use
`ff` followed by UInt32. Alignment bytes must be zero, including for empty typed arrays. Tag 5
(large integers) and all unknown tags are rejected. Empty payloads are invalid: null is `00`.
Decoders require exactly one complete value and reject trailing data. Container counts are checked
against remaining bytes before allocating storage. Payload and depth limits apply during encoding
and decoding. Name limits still belong to the transport.

Examples: Int32 42 is `03 2a 00 00 00`; an empty Int32 array is `09 00 00 00`; a two-byte array is
`08 02 01 ff`. Float64 1.25 is `06 00 00 00 00 00 00 00 00 00 00 00 00 00 f4 3f`.
Shared exact-byte and malformed-input fixtures are in `tests/fixtures/standard-values.json`.

The value tag/alignment conventions follow the [standard codec description](https://api.flutter.dev/flutter/services/StandardMessageCodec-class.html).
NativeChannels specifies its own transport, top-level null representation, supported types, and strict
validation. No Flutter compatibility claim is made; JSON remains the default.

## Event subscriptions

EventChannel builds on ordinary JSON MethodChannel calls. The generic native binding is unchanged.
The subscriber registers a unique private endpoint named `__maui.events/` followed by a 32-digit
hexadecimal UUID, then invokes `listen` on the public event channel with this tagged argument map:

```json
{"v":1,"method":"listen","arguments":["map",{"events":["s","__maui.events/0123456789abcdef0123456789abcdef"],"arguments":["n"]}]}
```

The producer keeps this request pending for the subscription's lifetime. For each event it invokes
`event` on the private endpoint, with the event value as the method arguments. The receiver replies
with success/null after accepting or dropping the value according to its buffer policy. Producers
sequence these acknowledgements to preserve event order; an acknowledgement is not a promise that
application code has processed the event.

After acknowledging all accepted deliveries, complete the original `listen` request with success/null
for normal completion, or a structured error for stream failure. This terminal response is separate
from event buffering. A null `event` argument remains ordinary data. Native queue overflow is a
terminal `event_overflow` error and discards unsent native values.

Cancel the original listen request to unsubscribe. The producer's cancellation callback detaches its
listener and cancels any in-flight event request. No extra correlation IDs, explicit cancel method,
or timeout is added to the method envelope. Private endpoints are removed on every terminal path;
late replies are ignored by the messenger. The `__maui.events/` prefix is reserved by ChannelService.
