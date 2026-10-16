# EventChannel

Use EventChannel for a stream of native values, such as sensor readings or SDK notifications.
Each enumeration starts its own native subscription, even when enumerating the same sequence twice.

The `app.readings` endpoint below belongs to your adapter. Register a native producer using the
Kotlin or Swift examples on this page before listening. For an existing implementation, see
[VolumeMonitor](volume-monitor.md).

## Listen from C#

```csharp
using var events = channels.GetEventChannel("app.readings");
await foreach (var reading in events.ListenAsync<double>(cancellationToken: cancellationToken))
{
    // Process each reading. Dispatch UI updates through MAUI's main thread when needed.
}
```

Pass subscription arguments as the first parameter of `ListenAsync<T>`. Typed values use the same
serializer and source-generated DTO metadata as MethodChannel. Use a nullable result type when the
producer can emit null. Null is an event; completion is a separate terminal signal.

Streams have **no invocation timeout**. Cancel the enumeration token, break out of `await foreach`,
or dispose its channel handle to unsubscribe. Disposing one handle does not affect subscriptions
owned by other handles. Cancellation is cooperative: the native adapter must detach its SDK listener.

## Bounded buffering

Each managed subscription buffers 64 events by default. Configure it per handle:

```csharp
using var events = channels.GetEventChannel("app.readings", new()
{
    BufferCapacity = 16,
    OverflowPolicy = EventOverflowPolicy.DropOldest,
});
```

| Policy | When the buffer is full |
| --- | --- |
| `Fail` (default) | Ends this subscription with `EventBufferOverflowException` and cancels its native producer. |
| `DropOldest` | Removes the oldest buffered event to accept the incoming event. |
| `DropNewest` | Discards the incoming event and preserves the current buffer. |

Capacity must be positive. Completion and errors are stored separately from the buffer and remain
deliverable when it is full. Accepted events drain before completion or an error is observed.
Cancellation and handle disposal interrupt reading immediately. Native errors retain their code,
message, details, and platform identity.

## Kotlin producer

Register the handler inside your explicitly configured native module:

```kotlin
EventChannel("app.readings", bridge).setStreamHandler { arguments, sink ->
    val listener = sensor.addListener { value -> sink.success(value) }
    sink.onTermination { sensor.removeListener(listener) }
}
```

Here `sensor` represents your SDK. Each invocation must create an independent listener. Call
`sink.endOfStream()` for normal completion or `sink.error("disconnected", "Sensor disconnected")`
for a terminal error. `onTermination` runs once on completion, error, cancellation, or bridge disposal,
including when registered after termination. Register one cleanup action and dispatch it yourself
if the SDK requires its UI thread.

## Swift producer

```swift
precondition(EventChannel("app.readings", bridge: bridge).setStreamHandler { arguments, sink in
    let listener = sensor.addListener { value in sink.success(value) }
    sink.onTermination { sensor.removeListener(listener) }
})
```

Call `sink.endOfStream()` or `sink.failure("disconnected", message: "Sensor disconnected")`.
The sample uses finite streams and independently cancellable subscriptions without requiring a
sensor or additional permissions. See [sample verification](sample.md).

The Kotlin and Swift sinks serialize delivery and retain at most 64 queued events plus one in flight.
Exceeding that native queue fails explicitly with code `event_overflow`. The managed overflow policy
does not change this native bound. Terminal signals need no queue slot. SDK callbacks should emit
bounded rates; these helpers are not intended for unbounded bulk transfer. Values must not be mutated
after sending them. Cleanup releases queued values and cancels any pending delivery.

## Managed producers

A managed peer can serve independent streams too:

```csharp
using var events = channels.GetEventChannel("app.readings");
using var registration = events.SetStreamHandler((arguments, cancellation) =>
    ProduceReadingsAsync(cancellation)); // IAsyncEnumerable<object?> supplied by your application
```

The handler runs once for each incoming subscription. Observe its cancellation token and release
resources in the iterator's `finally` block. Only one stream handler is allowed per endpoint;
registration tokens remove only the handler they own. Kotlin/Swift currently provide producer
helpers; consuming a managed producer natively requires the documented [event wire protocol](message-protocol.md#event-subscriptions).

EventChannel reuses the existing messenger and generic bridge; it does not add method-specific
C# bindings or introduce Flutter protocol compatibility.

## Volume observation example

[VolumeMonitor](../plugins/Plugin.Maui.VolumeMonitor/README.md) demonstrates a real native event source,
initial values, per-listener observers, cancellation cleanup, and a live MAUI display. Its verification
record distinguishes simulator subscription checks from actual system volume changes.
