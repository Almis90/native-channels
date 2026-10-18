# Channel diagnostics

::: info Availability
These APIs are available in `0.1.0-preview.2` and later.
See [getting started](getting-started.md) to install the package.
:::

Enable diagnostics to see which channel operations start, how long they take, and how they finish.
Diagnostics are disabled by default. Core has no logging-provider dependency; you choose where the
structured notifications go.

## Enable in MAUI

Configure the sink inside your existing `UseNativeChannels` call:

```csharp
using System.Diagnostics;
using NativeChannels;

builder.UseNativeChannels(options =>
{
    // Keep your existing module and view-factory registrations here.
    options.Diagnostics = new ChannelDiagnostics(entry =>
        Trace.WriteLine(
            $"{entry.OperationId} {entry.Direction} {entry.Kind} " +
            $"{entry.Channel}/{entry.Method} {entry.Stage} {entry.Outcome} " +
            $"{entry.Elapsed.TotalMilliseconds:F1} ms " +
            $"code={entry.ErrorCode} platform={entry.Platform}"));
});
```

The same sink receives notifications from the application service and each native view's instance
channel. Set it only in a debug configuration if you do not need production diagnostics. Configure
your application's trace listener to select the output destination.

For Core without MAUI, use the existing constructor and an initializer:

```csharp
await using var channels = new ChannelService(messenger)
{
    Diagnostics = new ChannelDiagnostics(entry => Console.WriteLine(entry))
};
```

Configure it before creating channel handles. Each service owns its configuration; enabling one
service does not enable unrelated services or install a global listener.

## What is reported

Every observed operation emits `Started`, followed by at most one `Completed` notification. An
operation that remains pending has no completion until it finishes, is cancelled, times out, or is
disposed. Match the pair using `OperationId`.

| Field | Meaning |
| --- | --- |
| `OperationId` | Local diagnostic ID, shared by the start and completion; independent of transport correlation IDs. |
| `Kind` | `Method` or `Message`. |
| `Direction` | `Outgoing` from this managed service or `Incoming` to its handler. |
| `Channel` / `Method` | Endpoint and method names. Messages have no method; incoming starts precede decoding, so their method is available at completion. |
| `Timestamp` / `Elapsed` | UTC notification time and monotonic duration. Elapsed time is zero at the start. |
| `Timeout` | Effective outgoing timeout after service, channel, and invocation precedence. Infinite remains `Timeout.InfiniteTimeSpan`; incoming handlers have no local deadline value. |
| `Outcome` | Completion result; null at the start. |
| `ErrorCode` / `Platform` | Structured `ChannelException` metadata when available. Platform is not inferred from the operating system. |

Completion distinguishes `Success`, `Cancelled`, `TimedOut`, `Disposed`, `ChannelNotFound`,
`NotImplemented`, `SerializationError`, `TransportError`, `ChannelError`, and other `Failed` operations.
A successful null is `Success`. An invalid typed result is `SerializationError`, even when transport
delivery succeeded. See [error handling](error-handling.md) for the exceptions returned to callers.

Outgoing duration includes serialization, waiting for the reply, decoding, and typed conversion.
Incoming duration includes decoding, dispatch, handler execution, and reply encoding, or ends when
the handler's cancellation token is signalled. Incoming cancellation cannot distinguish a remote
timeout from remote disposal. It does not prove an SDK operation stopped. Late handler completions
do not emit a second completion. These durations are not isolated bridge latency measurements.

## Coverage

- `MethodChannel`: outgoing invocations and incoming managed handlers, including per-view channels.
- `BasicMessageChannel`: outgoing sends and incoming managed handlers, including binary codecs.
- `EventChannel`: its underlying `listen` and `event` method operations. The listen operation lasts
  until the subscription ends; individual deliveries use internal subscription channel names.

This observes the managed API boundary. It does not add native Kotlin/Swift loggers, trace raw
`IBinaryMessenger` calls, or report startup/cleanup failures and native view rendering. Event buffer
metrics, dropped-value counts, and consumer-side event type conversions are not reported. Existing
cleanup traces and platform logs remain useful for those cases.

## Use your existing logger

An application with an `ILogger` can forward completed notifications. For example, this helper uses
the application's logging providers and category:

```csharp
using Microsoft.Extensions.Logging;
using NativeChannels;

static ChannelDiagnostics CreateDiagnostics(ILogger logger) => new(entry =>
{
    if (entry.Stage != ChannelDiagnosticStage.Completed)
        return;

    var level = entry.Outcome is ChannelDiagnosticOutcome.Success
        or ChannelDiagnosticOutcome.Cancelled
        ? LogLevel.Debug : LogLevel.Warning;

    if (!logger.IsEnabled(level))
        return;

    logger.Log(level,
        "Channel {Channel} method {Method} {Direction} {Outcome} in {ElapsedMs} ms " +
        "(operation {OperationId}, code {ErrorCode}, platform {Platform})",
        entry.Channel, entry.Method, entry.Direction, entry.Outcome,
        entry.Elapsed.TotalMilliseconds, entry.OperationId, entry.ErrorCode, entry.Platform);
});
```

Assign the returned instance to `options.Diagnostics` or `ChannelService.Diagnostics`. For high-rate
audio or event traffic, filter or sample notifications before writing logs. If you forward them to a
queue, use bounded buffering and an explicit overflow policy in your application.

## Privacy and observer behavior

Notifications contain **no arguments, results, raw bytes, exception objects, exception messages,
stack traces, or error details**. There is no payload-logging switch. Channel names, method names,
error codes, and platform labels are application-defined metadata: do not put credentials or user
data in them.

The observer runs synchronously on the current caller, background handler, or cancellation thread.
Multiple operations can notify concurrently. Keep it thread-safe and nonblocking; do not call back
into channels or update UI directly from it. A slow observer adds latency to the application.
Observer exceptions are ignored so a broken logging provider cannot replace a result or prevent
request cleanup. When diagnostics are unset, no diagnostic records, IDs, timers, or cancellation
registrations are created.
