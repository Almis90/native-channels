# Configuration

`UseNativeChannels` configures the application-wide service. Call it once before `builder.Build()`;
a duplicate registration throws. Resolve `IChannelService` from DI after startup.

```csharp
using NativeChannels;

builder.UseNativeChannels(options =>
{
    options.Modules.Add(new NativeModuleRegistration(
        "dev.nativechannels.sample.SampleModule", "MCSampleModule", "NativeChannelsSample"));
    options.DefaultTimeout = TimeSpan.FromSeconds(45);
});
```

For opt-in operation logging in the current source, configure `options.Diagnostics`.
See [channel diagnostics](diagnostics.md) for setup, privacy, and availability.

## Timeout precedence

| Level | Setting | Default |
| --- | --- | --- |
| Service | `NativeChannelsOptions.DefaultTimeout` | 30 seconds |
| Handle | `MethodChannelOptions.Timeout` | Inherit service setting |
| Invocation | `ChannelInvocationOptions.Timeout` | Inherit handle setting |

A non-null invocation value wins over the handle, which wins over the service.
`Timeout.InfiniteTimeSpan` is an explicit override, not an omitted value.

```csharp
using var channel = channels.GetMethodChannel("sample", new MethodChannelOptions
{
    Timeout = TimeSpan.FromSeconds(60)
});

// Use the handle's 60-second deadline.
await channel.InvokeAsync("delay", 100);

// Disable the deadline for this call; an application token can still cancel it.
await channel.InvokeAsync("delay", 100,
    cancellationToken: cancellationToken,
    options: new ChannelInvocationOptions { Timeout = Timeout.InfiniteTimeSpan });
```

Use extended or infinite deadlines deliberately for interactive SDK flows such as authentication,
payment screens, and pairing. This does not mean those SDK integrations are supplied by NativeChannels.
Zero, negative durations other than infinite, and durations beyond the .NET timer range are rejected.
The deadline starts in the messenger after method argument encoding.

## Cancellation

Pass the feature's cancellation token to each invocation. Cancelling ends the managed wait and signals
the native request context. An adapter must connect that signal to the SDK's cancellation API if one
exists. Late replies are ignored. Handle disposal cancels that handle's outstanding calls; session
disposal ends all calls in the session. See [lifecycle](lifecycle.md).

## UI dispatch

Incoming managed handlers normally enter on a background dispatcher. Configure a handle whose
incoming handler must enter on the MAUI UI thread:

```csharp
using var channel = channels.GetMethodChannel("sample", new MethodChannelOptions
{
    HandlerDispatcher = MauiChannelDispatchers.MainThread
});
```

This setting controls incoming managed handler entry. Configure native UI handlers separately with
Kotlin `setHandler(onMainThread = true)` or Swift `setHandler(onMainThread: true, handler:)`.
See [threading](threading.md) for asynchronous continuation and reentrancy rules.

## Registration without a MAUI host

The platform package exposes shared registration through `IServiceCollection`. For example, a
platform-specific application can register Currency using the same plugin archive as a MAUI app:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Currency;

var services = new ServiceCollection();
services.AddNativeChannels(options => options.AddCurrency());
services.AddCurrency();
await using var provider = services.BuildServiceProvider();
var currency = provider.GetRequiredService<ICurrencyService>();
string symbol = await currency.GetSymbolAsync("EUR");
```

Reference `Plugin.Currency` and `Microsoft.Extensions.DependencyInjection`; use a supported
platform target such as `net10.0-windows10.0.19041.0` or `net10.0-android`. The platform package and
native adapters arrive through the plugin dependency. This API supplies channel services; the host
still controls startup, UI dispatch, and disposal. The
[Avalonia Android sample](../samples/Avalonia/Currency/Currency.Android/README.md) demonstrates activity-owned
Currency sessions, while the [iOS sample](../samples/Avalonia/Currency/Currency.iOS/README.md) owns one session
for its application view. The [Windows sample](../samples/Avalonia/Currency/Currency.Desktop/Windows.md)
owns one desktop window and awaits session cleanup on normal close. A general Avalonia application
host package is not supplied.

Registration snapshots module entries, timeout, immutable limits, and a copy of the JSON options.
Every service provider owns a separate, lazily created session. Use asynchronous provider disposal
to await native cleanup. Do not combine `AddNativeChannels` and `UseNativeChannels` on the same
service collection. MAUI applications continue to use `UseNativeChannels`, which initializes the
session at startup and retains `options.ViewFactories` for MAUI view hosting.

## Serialization metadata

Set `options.Json.TypeInfoResolver` to your source-generated `JsonSerializerContext` when exchanging
DTOs. Use consistent property naming on both sides. Primitive values and string-keyed maps need no
DTO metadata. The complete example and supported contracts are in [codecs](message-codecs.md).

## Limits

| Managed limit | Default |
| --- | --- |
| `MaximumPayloadBytes` | 16 × 1024 × 1024 bytes |
| `MaximumDepth` | 64 |
| `MaximumNameBytes` | 256 UTF-8 bytes |

Configure these through `options.Limits`, for example to impose a smaller managed payload cap:

```csharp
options.Limits = new ChannelLimits { MaximumPayloadBytes = 1024 * 1024 };
```

Names cannot be blank or contain control characters. UTF-8 byte length is not the same as character
count. The native JSON codecs retain their own protocol limits; raising a managed limit does not
raise the native limit. Keep both sides compatible and avoid sending large files as channel payloads.

## Custom codecs

`MethodChannelOptions.Codec` selects a method codec for a handle. Every live handle to the same named
endpoint must use the same codec instance; incompatible registrations throw. Both endpoints must
agree on the wire format. Native MethodChannel helpers implement the built-in tagged JSON format.
Changing the managed codec alone will not change their protocol.

## Messages and streams

`GetBasicMessageChannel` accepts `BasicMessageChannelOptions` with `Codec`, `Timeout`, and
`HandlerDispatcher`. Its send operations use the same timeout precedence as method calls.

`GetEventChannel` accepts `EventChannelOptions` with `BufferCapacity` (64 by default) and
`OverflowPolicy` (`Fail` by default). Each enumeration owns that buffer. Streams have no invocation
timeout; dispose or cancel the enumeration to unsubscribe. Buffer options can differ between handles
because each subscription is independent. See [basic messages](basic-message-channels.md) and
[event streams](event-channels.md).

## Native views

`PlatformChannelView` owns a separate native session for each connection. View creation, attachment,
disconnection, and reconnection require the UI thread. Configure `options.ViewFactories` explicitly;
reconnect creates a new instance and invalidates the old channel. See [native views](native-views.md).
