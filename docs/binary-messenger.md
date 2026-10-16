# Binary messenger and transport

`IBinaryMessenger` sends named binary requests and registers asynchronous incoming handlers.
`BinaryMessenger` owns correlation IDs, pending state, cancellation, timeout timers, handler ownership,
and reply completion. `IChannelTransport` carries request frames and response statuses; production
`NativeChannelTransport` implements it using the generic Kotlin or Swift bridge.

`InMemoryTransport.CreatePair()` provides portable bidirectional endpoints for tests and nonplatform
experiments. Construct a messenger and `ChannelService` for each endpoint. The portable core solution
never requires MAUI workloads.

The messenger queues handler entry in FIFO order. An asynchronous handler releases the dispatcher at
its first suspension, allowing reentrant calls and out-of-order completion. Pending entries are removed
on success, error, cancellation, timeout, and disposal. `PendingRequestCount` supports diagnostics.
Service disposal owns its messenger, which owns its transport. Use `DisposeAsync` when teardown must
be complete before continuing. Synchronous DI disposal initiates asynchronous native draining.
