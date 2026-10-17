# NativeChannels

[![Documentation](https://img.shields.io/badge/docs-GitHub%20Pages-6554c0)](https://almis90.github.io/native-channels/)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](docs/building.md)
[![MIT license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Native integrations for .NET MAUI, through one reusable bridge.**

NativeChannels connects C# with Kotlin, Swift, and Windows APIs through asynchronous calls, messages, event streams,
and embedded native views. Keep SDK operations in a native adapter and expose a typed C# API to
your application. Each operation travels through the generic bridge without a method-specific C# binding.

With a native module registered, inject `IChannelService` and call its named operations:

```csharp
using var channel = channels.GetMethodChannel("sample");
string? reply = await channel.InvokeAsync<string>("echo", "Hello from .NET");
// The bundled Kotlin or Swift sample adapter returns "Hello from .NET".
```

**Preview software.** NativeChannels supports Android, iOS, Mac Catalyst, and Windows through its
MAUI integration. The core messaging layer is independent of MAUI. Complete Avalonia integration is
planned for 0.2 and Uno for 0.3; neither is available on `main` yet.
The first preview is [available on NuGet.org](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.1).

[Documentation](https://almis90.github.io/native-channels/) ·
[Getting started](docs/getting-started.md) ·
[Build a plugin](docs/plugin-authoring.md) ·
[Examples](docs/examples.md)

## What you can build

| Contract | API | Reference example |
| --- | --- | --- |
| Named operations with typed results and structured errors | `MethodChannel` | [Currency](plugins/Plugin.Maui.Currency/README.md): currency code → native symbol |
| Values or binary chunks with an asynchronous reply | `BasicMessageChannel` | [AudioStreamer](plugins/Plugin.Maui.AudioStreamer/README.md): PCM playback through AudioTrack / AVAudioEngine / Windows waveOut |
| Native observations with independent subscriptions | `EventChannel` | [VolumeMonitor](plugins/Plugin.Maui.VolumeMonitor/README.md): volume levels and listener cleanup |
| Embedded native UI with an instance-specific channel | `PlatformChannelView` | [PdfViewer](plugins/Plugin.Maui.PdfViewer/README.md): PDFKit / PdfRenderer / Windows.Data.Pdf with navigation and zoom |

All four examples include native adapters and a separate MAUI application that consumes local packages.
They are reference implementations for plugin authors. Start with the
[channel selection guide](docs/choosing-a-channel.md) to choose a contract for your integration.

## How it works

```text
C# application or plugin
        ↕  named channels
NativeChannels messenger + codecs
        ↕  platform transports and generic bindings
Kotlin, Swift, or Windows C# adapter
        ↕
Native SDK
```

Both sides agree on names and supported values. The messenger handles requests, correlation,
cancellation, and timeouts; codecs encode the payloads. Native modules register explicitly at startup.
Native code can also call C# and await a reply.

An integration can live inside your application or in a reusable plugin containing its C# facade,
native adapters, and package imports. The consuming application then configures the plugin once.
NativeChannels supplies the communication layer; you still provide native SDK dependencies and
platform-specific lifecycle behavior. See [architecture](docs/architecture.md).

## Get started

Add the package to a .NET 10 MAUI application:

```sh
dotnet add package NativeChannels.Maui --version 0.1.0-preview.1
```

Core, platform transports, and the applicable Android or Apple binding are transitive dependencies.
Windows support is included in `NativeChannels` and `NativeChannels.Maui`. You still provide and
register the native adapter for your SDK; installing the bridge alone does not add SDK operations.

The [getting-started guide](docs/getting-started.md) walks through package installation,
adapter registration, and a complete native round trip. Then follow one path:

- **Try the bridge:** run the [framework verification sample](samples/NativeChannels.Sample/README.md).
- **Integrate a native SDK:** follow [getting started](docs/getting-started.md), then the
  [Android](docs/android-integration.md) and [Apple](docs/ios-integration.md) adapter guides.
- **Build a reusable plugin:** follow [plugin authoring](docs/plugin-authoring.md) and study the
  [Currency reference implementation](plugins/Plugin.Maui.Currency/README.md).

Repository samples use local feeds to test source changes. For those feeds, device commands, and
the pinned toolchain, see [building from source](docs/building.md).

## Behavior you can rely on

- **Independent ownership.** Disposing a channel handle cancels its calls and removes its
  registrations; other handles for the same endpoint remain usable.
- **Async communication.** Incoming handlers enter in FIFO order; their replies may complete out of order.
  Reentrant native-to-managed calls are supported.
- **Explicit deadlines.** Requests default to 30 seconds. Override per service, channel, or call;
  `Timeout.InfiniteTimeSpan` disables the timer. Cancellation signals cooperative remote cancellation.
- **Typed values.** The default tagged JSON codec preserves signed integers, bytes, lists, and maps.
  DTOs use configured `System.Text.Json` metadata, including source-generated contexts for trimmed builds.
- **Bounded streams.** Every event enumeration owns a subscription. Its default buffer holds 64
  values and fails explicitly on overflow; optional drop policies are available.
- **View lifecycle.** Every connected native view has its own instance and method channel.
  Reconnecting creates a new instance and invalidates the old channel. View operations require the UI thread.

See [configuration](docs/configuration.md), [lifecycle](docs/lifecycle.md), and
[error handling](docs/error-handling.md) for the detailed contracts.

## Platform support

| Platform | Integration |
| --- | --- |
| Android | Kotlin/Java adapters and native Android views |
| iOS | Swift/Objective-C adapters and UIKit views |
| Mac Catalyst | Apple adapters and UIKit views on macOS |
| Windows | C# adapters for WinRT, COM, and native libraries; WinUI views |

MAUI is the current application host. Mac Catalyst support does not provide an AppKit desktop host.
See [platform verification](docs/verification.md) for executed scenarios and remaining gaps,
and the [roadmap](docs/roadmap.md) for planned Avalonia (0.2) and Uno (0.3) integration across Android,
iOS, Windows, desktop macOS, Linux, and browser/WebAssembly. These planned targets do not extend
the current support matrix; browser integration will require JavaScript adapters and DOM views.

## Documentation

| Task | Guide |
| --- | --- |
| Make your first native call | [Getting started](docs/getting-started.md) |
| Choose calls, messages, streams, or views | [Choose a channel](docs/choosing-a-channel.md) |
| Package an integration for other applications | [Build a plugin](docs/plugin-authoring.md) |
| Configure codecs, DTOs, timeouts, and dispatch | [Codecs](docs/message-codecs.md) · [Configuration](docs/configuration.md) |
| Diagnose startup or runtime failures | [Troubleshooting](docs/troubleshooting.md) |
| Explore the public C# surface | [API reference](docs/api-reference.md) |

The [documentation website](https://almis90.github.io/native-channels/) includes searchable guides,
native examples, and a [complete index](docs/overview.md).

## Development

The portable solution needs no mobile workloads:

```sh
bash eng/bootstrap.sh
bash eng/dotnet.sh test -c Release
```

Use `eng/NativeChannels.Platforms.slnx` for platform libraries. Changes to native bridges, bindings,
dependencies, or packaging require fresh package-consumer verification.
See [contributing](CONTRIBUTING.md), [testing](docs/testing.md), and [performance](docs/performance.md).

The [roadmap](docs/roadmap.md) covers the initial preview, complete Avalonia and Uno integration,
and stable API acceptance. The Currency samples for
[Avalonia Android](samples/Currency.Avalonia.Android/README.md) and
[Avalonia iOS](samples/Currency.Avalonia.iOS/README.md) cover the method-call slice of the milestone.
Use the [release checklist](docs/release-checklist.md) before distribution.

Pull requests run portable tests and compile affected platforms using one shared Core build.
Use **Verify framework** for development checks and **Verify distribution** to assemble final packages
and test their consumers before distribution. Example plugins and benchmarks remain separate, optional
workflows; see [CI commands](docs/building.md#github-actions). Verification workflows do not publish packages. The separate manual
[NuGet publishing workflow](docs/publishing.md) defaults to a dry run and requires approval to publish.

## License

[MIT](LICENSE). Inspired by [Flutter platform channels](https://docs.flutter.dev/platform-integration/platform-channels)
and [Redth's MAUI platform channels](https://github.com/Redth/Microsoft.Maui.Platform.Channels).
