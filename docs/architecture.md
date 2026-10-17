# Architecture

NativeChannels is a .NET-native library inspired by [Flutter platform channels](https://docs.flutter.dev/platform-integration/platform-channels)
and [Redth's platform-channel bridge](https://github.com/Redth/Microsoft.Maui.Platform.Channels).
It does not contain Flutter, Dart, or copied implementation code from those projects.

## Native interoperability

Applications communicate through generic byte-oriented Android, Apple, and Windows bridges. Native modules
register explicitly with an instance-scoped session; adapters implement SDK-specific operations
without generating a C# binding for each method.

Android and Apple adapters are included explicitly as an unbound AAR or native XCFramework.
Windows adapters implement `IWindowsChannelModule` in C# and can call packaged native libraries
through normal .NET interop. Packaging a generic bridge
does not remove the application's obligation to package third-party SDKs and native adapters.
The public sample consumes NuGet packages without library project references to verify distribution as well
as communication. A lower-level bridge regression consumer lives in `tests/PackageConsumer`.

## Package boundaries

`NativeChannels.Core` has no MAUI or native runtime dependencies. It owns named endpoints,
request correlation, cancellation, timeout handling, serialization, and channel contracts.
`NativeChannels` supplies platform transports, shared service registration, and native module configuration.
`NativeChannels.Maui` supplies DI/startup integration and native view handlers.
The generic Android and Apple bindings are separate implementation packages.

Native sessions are instance-scoped. Explicit module registration replaces global discovery.
Kotlin modules implement `NativeModule`; Swift modules implement the Objective-C-compatible
`MCNativeModule` protocol. Only configured modules are instantiated. Apple adapter frameworks
must be loaded before resolving their Objective-C class names. Windows modules are created through
explicitly registered factories; no assembly scanning is required.

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

The core messaging layer is independent of MAUI. `AddNativeChannels` registers a session from
`ChannelRegistrationOptions` in the platform package. MAUI's `UseNativeChannels` delegates to that
registration and adds startup integration and native view handlers. Its existing
`NativeChannelsOptions` extends the shared settings with MAUI view factories.

Currency depends on the platform package rather than the MAUI host. Applications choose their host
explicitly; the MAUI Currency sample references both `Plugin.Maui.Currency` and `NativeChannels.Maui`.
The [Avalonia Android sample](../samples/Avalonia/Currency/Currency.Android/README.md) consumes the same Currency
package through shared registration and owns disposal at the activity boundary. It is a method-call
proof; a general Avalonia host and native view integration remain [roadmap](roadmap.md) work.
The [iOS sample](../samples/Avalonia/Currency/Currency.iOS/README.md) shares the view and session code, uses
Avalonia's single-view application lifetime, and verifies its own Apple package/runtime boundary.
The [Windows sample](../samples/Avalonia/Currency/Currency.Desktop/Windows.md) uses the same view and session
with Avalonia's Win32 desktop backend. It reuses the Windows transport and Currency adapter without
depending on MAUI or WinUI; native view embedding remains separate work.

The [roadmap](roadmap.md) plans complete Avalonia
integration in 0.2 and Uno integration in 0.3, each covering Android, iOS, Windows, desktop macOS,
Linux, and browser/WebAssembly. The Android Currency proof is an initial part of the Avalonia
milestone, not full host or platform acceptance.

Shared registration and plugin contracts remain independent of application frameworks.
Each host must supply startup/lifecycle integration, dispatchers, and native or DOM view containers.
Browser support requires JavaScript adapters and web asset packaging, with an explicit capability
matrix; desktop macOS and Linux require their own native integration. The existing MAUI handlers
and OS adapters do not establish support for these planned hosts. No placeholder APIs are shipped.
