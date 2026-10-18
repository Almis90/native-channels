# Frequently asked questions

## Do I still need native code?

Yes. A native module translates your named method calls into SDK operations and returns supported
values. NativeChannels supplies the generic communication layer. Start from the bundled Kotlin, Swift, or Windows C#
sample module and use the native SDK's own APIs inside your adapter.

## Does this replace all .NET bindings?

The generic bridge is itself bound to .NET. SDK-specific operations live in the native adapter, so
they do not each need a C# binding. The application still packages the adapter, SDK binaries, and
runtime dependencies. Native types are not passed directly as channel values.

## Can I install this from NuGet.org?

Yes. Add [NativeChannels.Maui](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.2)
version `0.1.0-preview.2` to your .NET 10 MAUI application. Its framework dependencies resolve
automatically. Follow [getting started](getting-started.md) to include and register your native adapter.
The reference plugins remain local examples rather than published NuGet products.

## Can I use it without a phone or simulator?

You can run the portable core and in-memory tests on .NET 10 without MAUI workloads. Native SDK calls
require the relevant platform runtime. See [testing](testing.md).

## Can I exchange my own DTOs?

Yes, through explicit System.Text.Json metadata, including source-generated contexts for trimmed
builds. Match property names and supported value types on both sides. Arbitrary native objects,
polymorphic DTO contracts, and outgoing custom converter contracts are not currently supported.
See [typed values](message-codecs.md).

## What happens if a native operation takes longer than 30 seconds?

The default managed invocation times out. Choose a longer or infinite timeout for that operation,
and connect cancellation to the SDK when possible. Native-to-managed calls have a cancellable request
ID; they do not inherit the managed invocation default. See [configuration](configuration.md).

## Can two features use the same channel name?

Yes. Their handles own calls independently. The endpoint has only one incoming handler, so coordinate
which feature registers it. Codec configurations must also match. Disposing a handle leaves other
handles usable; disposing the service ends the whole session.

## Does app backgrounding automatically stop calls?

No. Feature owners decide what survives suspension and cancel or dispose accordingly. There is no
process-death persistence. See [lifecycle](lifecycle.md).

## Is Windows supported?

Windows provides module factories, C# native adapters, and WinUI views.
See [Windows integration](windows-integration.md) for setup and the
[verification record](verification.md) for tested configurations.

## Does a simulator pass prove device support?

No. Device builds and physical-device execution are separate checks, as are iOS and Mac Catalyst.
Consult the [verification matrix](verification.md) for the exact tested targets.

## Can I package an integration for other applications?

Yes. Package a managed facade, native adapters, dependencies, and `buildTransitive` imports together.
The consuming application configures the plugin's startup extensions and uses its C# API.
See [plugin authoring](plugin-authoring.md) and the [reference examples](examples.md).
