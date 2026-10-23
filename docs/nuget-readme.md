# NativeChannels

Native integrations for .NET MAUI and Avalonia through asynchronous method calls, binary/value messages,
event streams, and embedded native views. Keep SDK-specific work in Kotlin, Swift, or Windows
adapters and expose a typed C# API through the reusable bridge.

## Install

Add `NativeChannels.Maui` to a .NET 10 MAUI application. Select the desired preview version from
your configured feed; the framework, Core, and applicable native bindings are transitive dependencies.

```sh
dotnet add package NativeChannels.Maui --prerelease
```

The portable `NativeChannels.Core` package can also be used independently for messaging contracts,
codecs, serialization, and in-memory communication. Native SDK calls still require a platform host
and explicitly registered native modules. Installing the generic bridge does not install your SDK
or its adapters.

Follow the [getting-started guide](https://almis90.github.io/native-channels/getting-started)
for registration, native dependencies, and a complete round trip.

## Platform scope

Published preview `0.2.0-preview.1` provides MAUI integration for Android, iOS, Mac Catalyst,
and Windows, plus an Avalonia host for Android, iOS, Windows, native macOS, Linux, and
browser/WebAssembly. All five samples passed locally against the exact combined release
packages on the six Avalonia and four MAUI targets. Fresh public-feed Windows consumers
also passed. Broader device, architecture and lifecycle coverage remains incomplete.
Version `0.3.0-preview.1` is being prepared with the optional Uno host; see [0.3 preparation](preview-03.md).

See the [verification record](https://almis90.github.io/native-channels/verification) for exact
compiled targets, runtime evidence, physical-device observations, and remaining limitations.
Preview APIs can change before a stable release.

## Learn more

Opt-in [channel diagnostics](https://almis90.github.io/native-channels/diagnostics) report operation
timing, outcomes, and structured error codes without logging message contents. Enable them through
`NativeChannelsOptions.Diagnostics` or `ChannelService.Diagnostics`.

- [Choose a channel](https://almis90.github.io/native-channels/choosing-a-channel)
- [Build a reusable plugin](https://almis90.github.io/native-channels/plugin-authoring)
- [Configuration and timeouts](https://almis90.github.io/native-channels/configuration)
- [Lifecycle and ownership](https://almis90.github.io/native-channels/lifecycle)
- [Source and issues](https://github.com/Almis90/native-channels)

Licensed under [MIT](https://github.com/Almis90/native-channels/blob/main/LICENSE).
