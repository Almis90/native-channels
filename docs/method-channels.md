# MethodChannel

A method channel is a named request/response endpoint. `IChannelService.GetMethodChannel` returns an
independently disposable handle to it. Examples below use the bundled `sample` native module.

## Invoke a native method

```csharp
using var channel = channels.GetMethodChannel("sample");
var sum = await channel.InvokeAsync<int>("add", new Dictionary<string, object?>
{
    ["a"] = 10,
    ["b"] = 20
}, cancellationToken: cancellationToken);
// sum == 30
```

`InvokeAsync<T>` converts the result to `T`. `InvokeAsync` waits for completion but discards the
successful result. Arguments can be a supported scalar, byte array, list, string-keyed map, or a DTO
with configured metadata. See [typed values](message-codecs.md).

A successful null is distinct from an unknown method or a native error. For a nullable result:

```csharp
string? value = await channel.InvokeAsync<string?>("null");
```

Converting null to a nonnullable value type, such as `int`, throws a serialization error.

## Receive calls from native code

Keep the handle and its registration token alive for the period in which native code may call back:

```csharp
using var channel = channels.GetMethodChannel("sample");
using var registration = channel.SetMethodCallHandler((call, cancellation) =>
{
    cancellation.ThrowIfCancellationRequested();
    if (call.Method != "getToken") throw new MethodNotImplementedException(call.Method);
    return ValueTask.FromResult<object?>("demo-token");
});

var reply = await channel.InvokeAsync<string>("callback");
// reply == "native:demo-token"
```

Handlers can use `async` and await application work. Return a value (or `null`) for success. Throw
`MethodNotImplementedException` for an unknown method, or `ChannelException` for a structured error.
Unexpected exceptions produce a sanitized `managed_error`; see [error handling](error-handling.md).

To read incoming data, use `call.GetArgument<T>("name")` for one map entry or
`call.GetArguments<T>()` for the complete argument object. A missing named key produces
`ChannelException` with code `invalid_arguments`. Type conversion can produce
`ChannelSerializationException`.

## Own the registration

There is one incoming handler per endpoint within a session. Registering another throws; it does not
replace the existing handler. Dispose the registration token to remove just that registration, or call
`RemoveMethodCallHandler()` on its owning handle. A disposed token cannot remove a newer registration.

```csharp
using var first = channels.GetMethodChannel("sample");
using var second = channels.GetMethodChannel("sample");
first.Dispose();

// The second handle still works.
var value = await second.InvokeAsync<string>("echo", "still connected");
```

Different handles may choose different deadlines and incoming dispatch settings. All live handles
on the endpoint must share a compatible codec (the same codec instance when customized).

## Deadlines and cancellation

The default is 30 seconds. Service, handle, and invocation settings accept an explicit infinite
timeout; see [configuration](configuration.md#timeout-precedence) for precedence and examples.

```csharp
using var cancellation = new CancellationTokenSource();
var pending = channel.InvokeAsync("delay", 10_000, cancellation.Token,
    new ChannelInvocationOptions { Timeout = Timeout.InfiniteTimeSpan });
cancellation.Cancel();

try { await pending; }
catch (OperationCanceledException) { /* The caller stopped waiting. */ }
```

Cancellation signals cooperative native cancellation. It does not promise to stop an SDK operation.
Late responses are ignored. Native callers receive a request ID from `invoke` and can pass it to
`cancel(id)` when their operation ends; native calls do not automatically inherit a managed service's
default timeout.

## Ordering and cleanup

Sequential requests enter handlers in FIFO order, but asynchronous work can complete out of order.
Use `await`, rather than synchronously waiting on a remote call. Reentrant native → managed → native
calls are supported.

Dispose handles when their feature ends. Handle disposal removes owned registrations and cancels its
operations. Disposing the service tears down the entire messenger and transport session. See
[threading](threading.md) and [lifecycle](lifecycle.md).
