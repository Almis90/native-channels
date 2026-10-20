---
description: Explore Currency, AudioStreamer, VolumeMonitor, and PdfViewer reference plugins and their package-consuming MAUI samples.
---

# Examples

The repository includes four reference plugins and a framework verification app. Each plugin shows
a different native contract and has a separate MAUI application that consumes its local package.
Use the implementations as patterns for your own integration.

The [Uno sample suite](../samples/Uno/README.md) contains Interop, Currency,
AudioStreamer, VolumeMonitor and PdfViewer apps. It reuses the shared messaging
plugins and keeps the Uno host optional. Windows native PDF painting and lifecycle
checks now pass; broader visual/input review and six-platform acceptance
remain incomplete. See [Uno 0.3 status](preview-03.md).

## Pick an example

| Example | Native APIs | What it teaches | Guide |
| --- | --- | --- | --- |
| Currency | Android Currency / Apple NSLocale | Typed function calls, argument validation, structured errors, and packaged module registration | [Implementation](currency.md) · [Sample](currency-sample.md) |
| AudioStreamer | Android AudioTrack / Apple AVAudioEngine | Binary PCM transfer, acknowledgements, bounded queues, playback controls, and cleanup | [Implementation](audio-streamer.md) · [Sample](audio-sample.md) |
| VolumeMonitor | Android AudioManager / Apple AVAudioSession | Initial values, independent event subscriptions, observers, cancellation, and UI updates | [Implementation](volume-monitor.md) · [Sample](volume-sample.md) |
| PdfViewer | Android PdfRenderer / Apple PDFKit | Native view factories, per-instance channels, page navigation, zoom, callbacks, and reconnect | [Implementation](pdf-viewer.md) · [Sample](pdf-sample.md) |

Start with Currency for the smallest complete plugin. For channel selection and combinations, read
[choose a channel](choosing-a-channel.md). For the common package structure, read [plugin authoring](plugin-authoring.md).

## Build and run

1. Prepare the pinned toolchain and build the framework's local packages using [building](building.md).
2. Follow the chosen plugin's implementation guide to build its adapters and local package.
3. Follow its sample guide to restore into a fresh cache, build the target, and run the app.

Commands run from the repository root. Restore one target at a time: restore helpers clean that
sample's generated outputs. Choose the runtime identifier that matches your emulator or simulator.
Locally, run Android and iOS sequentially to avoid unnecessary resource use.

The plugins are examples in this repository, not published NuGet.org products. Their local packages
demonstrate how consumers receive the C# facade, framework dependencies, and native adapters together.
You can reuse the structure when packaging your own plugin.

## Know what each example proves

- **Currency:** symbols follow the native display locale. USD can return `$` or `US$`; this is a symbol lookup, not a currency converter.
- **AudioStreamer:** foreground playback of an original instrumental loop. It demonstrates PCM transport and buffering; background playback and production audio latency are outside its verified scope.
- **VolumeMonitor:** subscription setup and cleanup can be checked in simulators. Actual iPhone volume-change observation requires physical-device verification; Android notification delivery is best effort.
- **PdfViewer:** bundled, offline PDF viewing. Android renders raster pages; text search, selection, and annotations are outside the example's scope.

Each plugin records compilation and execution separately:
[Currency](currency-verification.md), [AudioStreamer](audio-verification.md),
[VolumeMonitor](volume-verification.md), and [PdfViewer](pdf-verification.md).

## Framework verification app

The [NativeChannels sample](sample.md) is an executable integration check. It exercises typed calls,
callbacks, codecs, errors, cancellation, timeouts, independent subscriptions, and native view lifetimes.
Its native buttons are small lifecycle fixtures. For a user-facing demonstration, choose one of the
four plugin samples above.

The framework app references packages and explicitly includes its native adapter artifacts.
The plugin samples reference their plugin package, which imports the adapters transitively.
That distinction makes both communication and packaging failures visible.
