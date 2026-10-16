---
description: Choose method calls, basic messages, event streams, or native views for your .NET MAUI integration.
---

# Choose a channel

Choose the contract that matches the interaction between your C# API and native adapter. One plugin
can combine several contracts: AudioStreamer uses method calls for playback controls and basic
messages for PCM data.

| Interaction | Use | Example |
| --- | --- | --- |
| Invoke a named operation and await a result | [MethodChannel](method-channels.md) | Look up a currency symbol, start authentication, pause playback |
| Exchange a value or byte buffer and receive a reply | [BasicMessageChannel](basic-message-channels.md) | Send a PCM chunk and acknowledge that it has been queued |
| Subscribe to values until completion or cancellation | [EventChannel](event-channels.md) | Observe system volume or SDK progress |
| Embed a native view with its own state and commands | [PlatformChannelView](native-views.md) | Host a PDF viewer with page navigation and native callbacks |

## Named operations: MethodChannel

Use a method channel when callers need named commands, typed results, or structured application
errors. The same endpoint supports native-to-managed requests.

With the bundled sample module configured:

```csharp
using var channel = channels.GetMethodChannel("sample");
int sum = await channel.InvokeAsync<int>("add", new Dictionary<string, object?>
{
    ["a"] = 10,
    ["b"] = 20,
});
```

The default JSON method codec distinguishes success, successful null, native errors, and an
unimplemented method. Use this contract for a plugin's control API. See [method channels](method-channels.md).

## Value exchange: BasicMessageChannel

Use a basic message channel when the payload itself is the contract. Each send has an asynchronous
reply, so a native adapter can acknowledge acceptance before C# sends more data.

With AudioStreamer's module configured, its PCM endpoint uses BinaryMessageCodec:

```csharp
using var stream = channels.GetBasicMessageChannel("plugin.maui.audio.pcm", new()
{
    Codec = BinaryMessageCodec.Instance,
});
await stream.SendAsync(pcmChunk, cancellationToken);
```

Here `pcmChunk` is a `byte[]` supplied by the application. Playback must be configured first;
see [AudioStreamer](audio-streamer.md) for the complete control/data flow. For your own adapter,
define the sample format, ordering, maximum chunk size, and acknowledgement meaning.
BinaryMessageCodec transports bytes; it does not encode or decode audio.

Basic replies do not carry a structured application-error envelope. Use method calls for commands
that need native error codes and details. See [basic messages](basic-message-channels.md).

## Subscriptions: EventChannel

Use an event channel when native code produces observations over time. With the VolumeMonitor
module configured, its endpoint emits an initial level and subsequent changes:

```csharp
using var volume = channels.GetEventChannel("plugin.maui.volume");
await foreach (var level in volume.ListenAsync<double>(cancellationToken: cancellationToken))
{
    // Update application state; dispatch UI work to the MAUI main thread.
}
```

Every enumeration creates an independent subscription. Cancellation, early exit, or handle disposal
unsubscribes it. Streams have no invocation timeout. The managed buffer defaults to 64 values with
explicit overflow failure; choose a drop policy only when losing readings is acceptable.
See [event streams](event-channels.md) for producer cleanup and terminal signals.

## Native UI: PlatformChannelView

Use a native view factory when the integration supplies a platform view. The MAUI control creates
one native instance per connection and exposes that instance's MethodChannel.

Register handlers when the view connects, invoke commands through its current channel, and release
observers and native resources on detach. Reconnection creates a fresh instance and invalidates
the previous channel. All view operations require the UI thread.
See [native views](native-views.md) and the [PDF example](pdf-viewer.md).

## Agree on values and ownership

Both endpoints must agree on channel names, channel kind, method names, and codec. Use names that
belong to your plugin, such as `com.example.payments`, to reduce collisions. Different live handles
for the same endpoint must use compatible configuration; only one incoming handler is allowed.

JSON is the default. Basic messages can also use string, binary, or standard binary value codecs.
Choose explicit shared types and configure source-generated metadata for DTOs in trimmed applications.
See [codecs](message-codecs.md), [configuration](configuration.md), and [lifecycle](lifecycle.md).
