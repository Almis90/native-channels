# Error handling

Handle failures according to their cause. A successful null, an unsupported method, an absent
endpoint, and an SDK error are distinct outcomes. Cancellation, timeout, and disposal use familiar
.NET exceptions.

| Condition | Managed result |
| --- | --- |
| Successful null | null, or serialization error for a nonnullable value target |
| Missing endpoint | `ChannelNotFoundException` |
| Unknown method | `MethodNotImplementedException` |
| Native error envelope | `ChannelException` with Code, Message, Details, Platform |
| Invalid value/envelope/typed result | `ChannelSerializationException` |
| Failed bridge/binary handler | `ChannelTransportException` |
| Caller cancellation | `OperationCanceledException` |
| Deadline | `TimeoutException` |
| Disposed handle/session | `ObjectDisposedException` |

Throw `ChannelException` from managed handlers for intentional structured errors. Unexpected exceptions
become a sanitized `managed_error` response; private exception details and stack traces are not sent.
Kotlin and Swift adapters use their `MethodResult` error/failure and not-implemented helpers.
Native startup failures name the missing module or framework and explain adapter packaging requirements.


## Handle expected failures

Catch specific channel exceptions before the general `ChannelException`, because they derive from it:

```csharp
try
{
    await channel.InvokeAsync("fail", cancellationToken: cancellationToken);
}
catch (MethodNotImplementedException)
{
    // This native adapter does not implement the requested operation.
}
catch (ChannelNotFoundException)
{
    // Check the module registration and channel name.
}
catch (ChannelException error) when (error.Code == "sample_error")
{
    // Inspect error.Platform and error.Details to present the application's failure state.
}
catch (TimeoutException)
{
    // The managed invocation exceeded its configured deadline.
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // The feature or caller cancelled its wait.
}
```

For an intentional error in a managed handler, throw
`new ChannelException("permission_denied", "Access is required.", platform: "managed")`.
Use stable machine-readable codes; messages are for people. Avoid returning a null result to represent
a failure, because null is a valid success value. See [MethodChannel](method-channels.md).

## Messages and event streams

BasicMessageChannel replies contain values and do not carry structured application errors. Local
codec failures raise `ChannelSerializationException`; native handler failures use a transport error.
Use MethodChannel for structured request/reply errors.

EventChannel delivers structured native stream errors after already accepted buffered events. Managed
buffer overflow throws `EventBufferOverflowException` (code `event_overflow`) and cancels that native
subscription. A native producer-queue overflow also uses `event_overflow` with native platform identity.
Cancellation interrupts reading immediately; disposing the handle produces `ObjectDisposedException`.
