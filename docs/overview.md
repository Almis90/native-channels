# Documentation guide

NativeChannels connects .NET MAUI to native adapters through one generic Android or Apple bridge.
Use these guides to integrate an SDK, package a reusable plugin, or contribute to the framework.

## Start with your task

| I want to… | Start here |
| --- | --- |
| Try a complete native round trip | [Getting started](getting-started.md) |
| Select calls, messages, streams, or views | [Choose a channel](choosing-a-channel.md) |
| See practical integrations | [Examples](examples.md) |
| Package an SDK behind a C# API | [Build a plugin](plugin-authoring.md) |
| Find a member or option | [C# API reference](api-reference.md) |
| Fix a setup or runtime failure | [Troubleshooting](troubleshooting.md) |

## Channel guides

| Contract | Guide |
| --- | --- |
| Named operations and typed replies | [Method channels](method-channels.md) |
| Values and binary chunks with a reply | [Basic messages](basic-message-channels.md) |
| Independent native subscriptions | [Event streams](event-channels.md) |
| Embedded native UI with per-instance channels | [Native views](native-views.md) |

Read [configuration](configuration.md) for timeouts and dispatch, [codecs](message-codecs.md) for
supported values and DTO metadata, and [error handling](error-handling.md) for failure semantics.
[Threading](threading.md) and [lifecycle](lifecycle.md) explain ordering, ownership, and cleanup.

## Native adapters and plugins

- [Android integration](android-integration.md): Kotlin modules, AARs, native dependencies, and preservation rules.
- [Apple integration](ios-integration.md): Swift modules, Objective-C names, XCFrameworks, and callback lifetimes.
- [Mac Catalyst](maccatalyst-integration.md): platform slices and separate build/runtime checks.
- [Windows](windows-integration.md): C# adapters, WinUI views, local packages and verification.
- [Plugin authoring](plugin-authoring.md): contracts, managed facades, registration, packaging, and consumer verification.

The [Currency](currency.md), [AudioStreamer](audio-streamer.md), [VolumeMonitor](volume-monitor.md),
and [PdfViewer](pdf-viewer.md) reference plugins each include a sample and verification record.

## Internals and maintenance

- [Architecture](architecture.md), [binary messenger](binary-messenger.md), and [wire protocol](message-protocol.md).
- [Building](building.md), [testing](testing.md), and [performance and stress](performance.md).
- [Platform verification](verification.md), [release checklist](release-checklist.md), and [changelog](changelog.md).
- [Contributing](contributing.md) and [editing the documentation site](documentation-site.md).
- [Roadmap](roadmap.md), including shared hosting, Avalonia integration, and release acceptance.
- [FAQ](faq.md) and [Flutter comparison](flutter-comparison.md).

Packages are built locally and
have not been published to NuGet.org. Use the [verification record](verification.md) to distinguish
compiled targets, executed scenarios, and remaining gaps.
