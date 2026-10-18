# C# API reference

The NativeChannels APIs below describe the current source tree. This is a focused reference for application
and plugin authors, with links to the implementation and detailed guides. Native adapter APIs are
covered in the [Android](android-integration.md), [Apple](ios-integration.md), and [Windows](windows-integration.md) guides.

## Channel service

Inject `IChannelService` after calling `AddNativeChannels` or MAUI's `UseNativeChannels`. Each channel factory returns an
independently disposable handle. The DI container owns the service.

<<< ../src/NativeChannels.Core/Channels/IChannelService.cs

```csharp
using var channel = channels.GetMethodChannel("sample");
string? result = await channel.InvokeAsync<string>("echo", "Hello");
```

See [MethodChannel](method-channels.md) for both call directions and [lifecycle](lifecycle.md) for ownership.

## MethodChannel

| Member | Behavior |
| --- | --- |
| `Name` | The named endpoint shared by compatible handles. |
| `InvokeAsync(method, arguments, cancellationToken, options)` | Awaits completion without returning the result value. |
| `InvokeAsync<T>(method, arguments, cancellationToken, options)` | Converts the reply using the configured serializer; returns `Task<T?>`. |
| `SetMethodCallHandler(handler)` | Registers one incoming async handler and returns its ownership token. |
| `RemoveMethodCallHandler()` | Removes this handle's incoming registration. |
| `Dispose()` / `DisposeAsync()` | Ends this handle's operations and removes its registrations. |

The handler signature is `Func<MethodCall, CancellationToken, ValueTask<object?>>`. Return the result
value, including a successful null, or throw a structured `ChannelException`. Retain the registration
token for as long as native code may invoke the handler.

[Source: MethodChannel](../src/NativeChannels.Core/Channels/MethodChannel.cs).

## BasicMessageChannel

| Member | Behavior |
| --- | --- |
| `SendAsync(message, cancellationToken, options)` | Sends a value and returns the decoded reply. |
| `SendAsync<T>(message, cancellationToken, options)` | Converts the reply with the configured serializer. |
| `SetMessageHandler(handler)` | Registers one asynchronous value/reply handler and returns its ownership token. |
| `RemoveMessageHandler()` | Removes this handle's incoming registration. |
| `Dispose()` / `DisposeAsync()` | Cancels this handle's requests and removes its handler. |

See [basic messages](basic-message-channels.md) for codecs, dispatch, and native helpers.

## EventChannel

| Member | Behavior |
| --- | --- |
| `ListenAsync<T>(arguments, cancellationToken)` | Returns an async sequence; each enumeration starts an independent subscription. |
| `SetStreamHandler(handler)` | Serves independent `IAsyncEnumerable<object?>` producers to remote subscribers. |
| `RemoveStreamHandler()` | Removes this handle's stream registration and cancels its active producers. |
| `Dispose()` / `DisposeAsync()` | Cancels this handle's subscriptions and removes its registration. |

`EventChannelOptions` controls `BufferCapacity` (64) and `OverflowPolicy` (`Fail`, `DropOldest`,
`DropNewest`). Streams have no invocation timeout. See [event streams](event-channels.md) for cleanup,
terminal signals, and Kotlin/Swift producers. `EventBufferOverflowException` has code `event_overflow`.

## Calls and options

`MethodCall.Method` identifies the operation. `Arguments` exposes the decoded value;
`GetArguments<T>()` converts the entire value and `GetArgument<T>(name)` reads a map field.

<<< ../src/NativeChannels.Core/Channels/ChannelOptions.cs

A nullable timeout means inherit. Precedence is invocation → channel → service. Use
`Timeout.InfiniteTimeSpan` explicitly to disable the timer. Zero and other negative values are rejected.
See [configuration](configuration.md) for timeouts and dispatchers.

## Startup and native registration

`IServiceCollection.AddNativeChannels(Action<ChannelRegistrationOptions>?)` registers a native session
without selecting a UI framework. An overload accepts an existing `ChannelRegistrationOptions`.
The options expose `Modules`, `DefaultTimeout`, `Limits`, `Json` metadata settings, and optional `Diagnostics`. Registration
captures these settings; later edits do not reconfigure the session.

Each service provider creates its own session when `IChannelService` or `IBinaryMessenger` is first
resolved and owns its disposal. Hosts should await provider disposal for completed native cleanup.
Register once: an existing channel service or messenger registration is rejected.

MAUI's `UseNativeChannels(Action<NativeChannelsOptions>?)` delegates to shared registration and adds
MAUI startup and native view handling. `NativeChannelsOptions` inherits the shared options and adds
`ViewFactories`. Existing MAUI configuration calls remain source-compatible.

<<< ../src/NativeChannels/NativeModuleRegistration.cs

Use fully qualified Android class names and the explicitly exported Objective-C class name. Apple
framework names omit the `.framework` extension. See the native guides for compiled asset packaging.

`MauiChannelDispatchers.MainThread` runs a managed incoming handler through MAUI's UI dispatcher.
See [threading](threading.md) for FIFO handler entry and asynchronous completion.

## Diagnostics

Assign a `ChannelDiagnostics` instance to `NativeChannelsOptions.Diagnostics`, or initialize
`ChannelService.Diagnostics` when using Core directly. Its observer receives metadata-only
`ChannelDiagnosticEvent` records for method/message starts and completions. Diagnostics are disabled
by default. See [diagnostics](diagnostics.md) for the fields, coverage, and callback threading contract.

## Native views

| Member | Contract |
| --- | --- |
| `PlatformChannelView.FactoryName` | Explicit registration name; configure before connecting. |
| `CreationParameters` | Value encoded with the configured JSON codec once per connection. |
| `InstanceId` / `Channel` | Current unique ID and MethodChannel, or null when disconnected. |
| `Connected` / `Disconnected` | Connection notifications on the UI thread. Reinstall handlers on each connection. |
| `Reconnect()` | Recreate the native instance and invalidate the old channel; requires a MAUI handler. |
| `DisconnectAsync()` | Remove content and await bridge cleanup. |

`NativeViewFactoryRegistration(Name, AndroidClass, AppleClass, AppleFramework)` is added to
`NativeChannelsOptions.ViewFactories`. `UseNativeChannels` installs `PlatformChannelViewHandler`.
All view operations require the UI thread. See [native views](native-views.md) for native factory
interfaces, packaging, sizing, and ownership.

## Serialization and errors

| Type | Responsibility |
| --- | --- |
| `IMessageCodec` / `JsonMessageCodec` | Encode and decode supported tagged values. |
| `IMethodCodec` / `JsonMethodCodec` | Encode calls, successful results, errors, and unimplemented methods. |
| `ChannelSerializer` | Convert typed values with configurable `System.Text.Json` metadata. |
| `ChannelLimits` | Validate payload size, name length, and nesting depth. |
| `ChannelException` | Preserve native `Code`, `Message`, `Details`, and `Platform`. |
| `ChannelNotFoundException` | Identify a missing named endpoint. |
| `MethodNotImplementedException` | Identify an unsupported operation on an existing endpoint. |
| `ChannelSerializationException` | Reject malformed or unsupported values and conversions. |
| `ChannelTransportException` | Report bridge or transport failures. |

Cancellation, timeouts, and disposed handles use standard .NET exceptions. See
[error handling](error-handling.md) and [typed values](message-codecs.md).

## Messenger and transport

| Type | Responsibility |
| --- | --- |
| `IBinaryMessenger` / `BinaryMessenger` | Named binary requests, incoming handlers, correlation IDs, and pending-request lifetimes. |
| `IChannelTransport` | Versioned requests, responses, cancellation, and transport disposal. |
| `NativeChannelTransport` | Connect the messenger to Kotlin, Swift, or Windows C# adapters. |
| `InMemoryTransport.CreatePair()` | Connect two isolated in-process endpoints; also used for portable tests. |

See [binary messenger](binary-messenger.md), [protocol](message-protocol.md), and [testing](testing.md)
for contracts and complete portable examples.

The [Windows target](windows-integration.md) adds `NativeModuleRegistration.WindowsFactory`
(`Func<IWindowsChannelModule>`), `IWindowsChannelModule.Register(IChannelService)` and module disposal.
For views, `NativeViewFactoryRegistration.WindowsFactory` is a
`Func<WindowsViewContext, WindowsNativeView>`. These members are available in Windows assemblies;
guard shared registration code with `#if WINDOWS`.

## Currency reference plugin

This optional example demonstrates a typed facade over MethodChannel. Its API is not part of
the NativeChannels API.

<<< ../plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/ICurrencyService.cs

Register the module with `options.AddCurrency()` inside the existing `UseNativeChannels` call,
and the singleton service with `builder.Services.AddCurrency()`. The plugin package includes its
native adapters. See [the reference plugin guide](currency.md) to study the implementation, build
its local package, and run the sample. It has not been published to NuGet.org.
