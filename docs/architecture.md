# Architecture

NativeChannels is a .NET-native library inspired by [Flutter platform channels](https://docs.flutter.dev/platform-integration/platform-channels)
and [Redth's platform-channel bridge](https://github.com/Redth/Microsoft.Maui.Platform.Channels).
It does not contain Flutter, Dart, or copied implementation code from those projects.

## Native interoperability

Applications communicate through generic byte-oriented Android and Apple bridges. Native modules
register explicitly with an instance-scoped session; adapters implement SDK-specific operations
without generating a C# binding for each method.

Adapters are included explicitly as an unbound AAR or native XCFramework. Packaging a generic bridge
does not remove the application's obligation to package third-party SDKs and native adapters.
The public sample consumes NuGet packages without project references to verify distribution as well
as communication. A lower-level bridge regression consumer lives in `tests/PackageConsumer`.

## Package boundaries

`NativeChannels.Core` has no MAUI or native runtime dependencies. It owns named endpoints,
request correlation, cancellation, timeout handling, serialization, and channel contracts.
`NativeChannels` supplies platform transports and native registration.
`NativeChannels.Maui` supplies DI/startup integration and native view handlers.
The generic Android and Apple bindings are separate implementation packages.

Native sessions are instance-scoped. Explicit module registration replaces global discovery.
Kotlin modules implement `NativeModule`; Swift modules implement the Objective-C-compatible
`MCNativeModule` protocol. Only configured modules are instantiated. Apple adapter frameworks
must be loaded before resolving their Objective-C class names.

## Messaging and ownership

- The messenger owns correlation IDs; method codecs own call/result envelopes.
- JSON values use explicit type tags, signed integer ranges, base64 bytes, and finite floating-point values.
- Codecs and transport are independent. Basic messages support JSON, string, binary, and standard
  binary values; the method envelope remains NativeChannels-specific JSON.
- Request/reply calls default to 30 seconds, with service/channel/call overrides and explicit infinite timeout.
- Event subscriptions keep a cancellable listen request open without a timeout; each event uses an acknowledged callback.
- Channel handles own their requests and registrations; disposing one does not close another handle.
- FIFO applies to handler entry, not asynchronous completion.
- Cancellation is cooperative and cannot guarantee cancellation of an arbitrary SDK operation.
- Buffers are copied at runtime boundaries. Callback roots remain alive until detached safely.
- Serialization of application DTOs supports explicit System.Text.Json metadata for trimmed builds.

## Messages and streams

Basic messages and independent event subscriptions use the same
messenger and bridge ABI. JSON, binary, and string basic-message codecs agree across C#,
Kotlin, and Swift. Each stream has a private callback endpoint, a bounded buffer, and a cancellable
listen request whose response carries completion or an error. No per-SDK bindings are introduced.

Every enumeration creates an independent native subscription. The default managed capacity is 64;
overflow terminates that subscription explicitly, with optional drop-oldest/drop-newest policies.
Terminal completion/error is separate from buffered events and remains deliverable when buffers are full.

## Native views

Explicit native view factories connect `PlatformChannelView` through
MAUI handlers. Each connection owns an isolated bridge and a generated instance MethodChannel.
Detaching disposes its channel and native instance; reconnecting assigns a fresh ID and channel.
No per-view method bindings are needed. Creation, mutation, and detach/reconnect operations run
on the UI thread. See [native views](native-views.md) for the adapter and ownership contracts.

`FactoryName` identifies a registered native view factory, independently of the instance channel.
Two views with the same factory name receive different instance IDs and MethodChannels.
`NativeViewHost` is the generic native interop host; the public MAUI control is `PlatformChannelView`.

## Additional application hosts

The core messaging layer is independent of MAUI. Framework-neutral registration and additional
application hosts are planned in the [roadmap](roadmap.md); no placeholder APIs are shipped.
