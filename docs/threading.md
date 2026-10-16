# Threading

Dispatch explicitly when the operation touches UI or an SDK with thread-affinity requirements.

| Operation | Threading contract |
| --- | --- |
| Managed incoming method/message handler | Background entry by default; opt into `MauiChannelDispatchers.MainThread` |
| Kotlin callback handler | Background entry by default; use `onMainThread = true` for UI |
| Swift callback handler | Background entry by default; use `onMainThread: true` for UI |
| Swift async handler | Runs in a Task; use `MainActor.run` for UI work |
| Native view creation, attach, disconnect, reconnect | UI thread required |
| Event consumption | Dispatch UI updates explicitly from the consumer |

## Handler entry and completion

Managed incoming handlers enter through a background dispatcher. FIFO entry is preserved for sequential
requests; asynchronous completion can occur in any order. No serial queue is held while awaiting a
remote response. Reentrant native → managed → native requests are supported.

Set `MethodChannelOptions.HandlerDispatcher = MauiChannelDispatchers.MainThread` for a managed UI
handler. Kotlin `setHandler(onMainThread = true)` and Swift `setHandler(onMainThread: true, handler:)`
enter native handlers on their main thread. Swift `setAsyncHandler` uses a background Task; use
`MainActor.run` inside that handler for UI work. A handler must not synchronously block waiting for a
remote call. Cancellation callbacks should be short and must not throw.

Native callbacks retain their peers until completion. Session disposal detaches handlers, cancels pending
requests, and waits for queued native handler entries before releasing the managed receiver peer.
The protocol does not promise to stop an SDK operation that ignores cooperative cancellation.

## Native views

`PlatformChannelView` owns a separate native session for each connection. View creation, attachment,
disconnection, and reconnection require the UI thread. Configure `options.ViewFactories` explicitly;
reconnect creates a new instance and invalidates the old channel. See [native views](native-views.md).
