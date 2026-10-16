# Lifecycle and ownership

Keep each resource alive for the feature that uses it and release it when that feature ends.

| Resource | Owner | When it ends |
| --- | --- | --- |
| App-wide `IChannelService` | MAUI dependency injection | Application service teardown |
| Channel handle | The service, page, or feature that creates it | Handle disposal cancels its operations and removes its registrations |
| Incoming registration token | The registering feature | Token or owning handle disposal removes that handler |
| Event enumeration | The subscriber | Cancellation, early exit, completion, error, or handle disposal |
| Native view instance/channel | The current view connection | Detach or reconnect invalidates that instance and channel |

## Channels and registrations

Each native transport is a session with explicit native modules. Startup installs module handlers and
checks bridge protocol compatibility. Avoid invoking managed callbacks during module registration,
before the managed messenger has attached its receiver.

A service shares endpoints across independently disposable method, message, and event handles. Dispose registration tokens
or their owning handles to remove handlers; disposing a token twice has no effect. Disposing one handle
cancels its calls without affecting another handle on the same endpoint.

Dispose the service asynchronously at the end of a manually created session. It disposes its messenger
and transport. DI owns registered singletons; synchronous container teardown starts asynchronous drain,
while `DisposeAsync` can be awaited when deterministic native cleanup is required. Running SDK operations
must observe cancellation through their native result context and release their own resources.

App backgrounding does not automatically cancel all channels. Feature owners choose whether a long
interactive operation survives suspension. No process-death persistence is provided.

## Event subscriptions

Each EventChannel enumeration owns a distinct subscription and bounded buffer. A terminal response
removes its private callback endpoint. Breaking out of `await foreach`, cancelling the enumeration,
or disposing its handle signals cooperative native cancellation. A producer must detach its SDK
listener in `EventSink.onTermination`; a managed producer should use its iterator's `finally` block.
Completion/errors drain accepted buffered events, while cancellation/disposal abort reads immediately.
See [event channels](event-channels.md) for overflow policies and native queue limits.

## Native views

`PlatformChannelView` owns a separate native session for each connection. View creation, attachment,
disconnection, and reconnection require the UI thread. Configure `options.ViewFactories` explicitly;
reconnect creates a new instance and invalidates the old channel. See [native views](native-views.md).
