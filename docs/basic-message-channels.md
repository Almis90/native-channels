# BasicMessageChannel

Use a basic message channel when the contract is a value and a reply, such as exchanging text,
bytes, or a configuration map. Use [MethodChannel](method-channels.md) when the contract has named
operations or structured application errors.

The [AudioStreamer example](audio-streamer.md) demonstrates bounded PCM chunks, native music playback,
and acknowledgements that apply backpressure.

## Send and receive

With the [bundled sample module registered](getting-started.md), inject `IChannelService`:

```csharp
using var messages = channels.GetBasicMessageChannel("sample.messages.json");
using var registration = messages.SetMessageHandler((message, cancellation) =>
    ValueTask.FromResult(message));

long reply = await messages.SendAsync<long>(long.MaxValue, cancellationToken);
```

This sample's native handler sends the value back to C# before replying to the original request.
The managed registration above handles that callback. It proves communication in both directions;
registering a managed handler alone does not create a native receiver.

Each direction supports an asynchronous reply. Incoming handlers may make a reentrant outgoing
request on the same channel. A successful null is a value, including with the string and binary codecs.
`SendAsync<T>` uses the same typed conversion and source-generated DTO metadata as MethodChannel.

One incoming handler is allowed per endpoint. The returned token owns that registration. Disposing
a handle cancels its own requests and removes its handler; other handles continue working.

## Choose a codec

The following `app.*` endpoints are contracts for your own native adapter; register matching native
handlers before sending. Both peers must select the same codec. JSON is the default. StandardMessageCodec encodes structured
values and typed numeric arrays in binary; string and binary codecs accept only text or bytes,
respectively, or null. See [codec types and limits](message-codecs.md#standardmessagecodec).

```csharp
using var text = channels.GetBasicMessageChannel("app.text", new()
{
    Codec = StringMessageCodec.Instance,
    Timeout = Timeout.InfiniteTimeSpan,
});
string? reply = await text.SendAsync<string>("Hello €", cancellationToken);

using var binary = channels.GetBasicMessageChannel("app.bytes", new()
{
    Codec = BinaryMessageCodec.Instance,
});
byte[]? bytes = await binary.SendAsync<byte[]>(new byte[] { 0, 127, 255 });

using var standard = channels.GetBasicMessageChannel("app.standard", new()
{
    Codec = StandardMessageCodec.Instance,
});
int[]? numbers = await standard.SendAsync<int[]>(new[] { 1, 2, 3 });
```

Live handles on one endpoint must share a codec instance and channel kind. Different names can use
different codecs. String encoding is strict UTF-8; malformed data is rejected. Binary and string
payloads include a one-byte null/present marker, described in the [protocol](message-protocol.md).

## Kotlin

Register this inside your `NativeModule.register` implementation:

```kotlin
val messages = BasicMessageChannel("app.text", bridge, StringMessageCodec)
messages.setHandler { text, result ->
    result.success(text)
}
// Native code can also initiate a request:
messages.send("Hello from Kotlin") { reply, error ->
    // Handle the reply or transport/codec error here.
}
```

Use `JsonMessageCodec` for tagged values and `BinaryMessageCodec` for `ByteArray`. Retain the request
ID returned by `send` to call `cancel(id)`. An asynchronous handler can use `result.onCancel` to detach
its SDK callback. Complete with `result.success(value)` or `result.failure()`.

## Swift

```swift
let messages = BasicMessageChannel("app.text", bridge: bridge, codec: StringMessageCodec())
precondition(messages.setHandler { text, result in
    result.success(text)
})
try messages.send("Hello from Swift") { reply, error in
    // Handle the reply or transport/codec error here.
}
```

Use `Data` with `BinaryMessageCodec()`. `setHandler` returns false for a conflicting registration.
The native handler lives until the bridge closes, just like a native MethodChannel handler.

## Deadlines, dispatch, and errors

Timeout precedence is per-send override → channel setting → service default (30 seconds).
`Timeout.InfiniteTimeSpan` disables the timer; cancellation remains available. Incoming C# handlers
can opt into `MauiChannelDispatchers.MainThread` through `BasicMessageChannelOptions.HandlerDispatcher`.
Native handlers enter on the bridge's background queue; explicitly dispatch UI work.

Basic replies have no application-error envelope. A missing endpoint raises `ChannelNotFoundException`;
handler failure uses the transport failure path. Local encoding or decoding failure raises
`ChannelSerializationException`. Use MethodChannel when callers need structured native error details.
