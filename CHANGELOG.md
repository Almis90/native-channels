# Changelog

## 0.2.0-preview.1 — Unreleased

- Framework-independent registration and diagnostics, with a reusable Avalonia host for
  owned sessions, UI dispatch, and native/DOM view lifetimes.
- Avalonia implementations of ChannelExplorer/Interop, Currency, AudioStreamer, VolumeMonitor,
  and PdfViewer for Android, iOS, Windows, macOS, Linux, and browser/WebAssembly.
- Linux native adapters use C++; PDF examples provide separate Qt and GTK backends.
- This is the development version on `main`, not a published or accepted 0.2 release.
  Platform runtime verification and final package acceptance remain incomplete.

## 0.1.0-preview.2 — 2026-10-17

- Opt-in, metadata-only diagnostics for method calls, basic messages, event protocol operations,
  and per-instance native view channels. Reports starts, duration, outcomes, and structured error codes
  without payloads or exception messages. Logging failures do not affect channel results or cleanup.
- NuGet packages use the documentation website as their project link, embed the NativeChannels icon,
  and include package-specific descriptions and release notes.
- Managed public API comparisons against the hash-pinned published preview, with a fast Core check
  and a required all-platform comparison of final distribution packages.
- Optional native round-trip profiling and longer lifecycle soaks in the package-only sample,
  including OS/native memory counters, raw results and a separate manual workflow using verified feeds.
- Published all five framework packages and managed symbols to NuGet.org after exact-source framework,
  distribution and API compatibility gates. Clean public-feed Core execution and Android MAUI Release
  installation checks passed.

## 0.1.0-preview.1 — 2026-10-17

- Method calls, basic messages, event streams, and native views through a shared channel API.
- Android, iOS, Mac Catalyst, and Windows transports with explicit native module registration.
- Typed values and results, structured errors, cancellation, configurable timeouts, and independent ownership.
- MAUI startup integration, dispatchers, and per-instance native view channels.
- Currency, AudioStreamer, VolumeMonitor, and PdfViewer reference plugins with package-only applications.
- Portable tests, native protocol fixtures, lifecycle stress tests, and package inspection tools.
- Fast automatic checks and manual full-platform verification.
- Manual NuGet release preparation with exact-source distribution evidence, package hashes, symbols,
  and Trusted Publishing authentication behind environment approval. Dry runs do not authenticate or publish.
- Published the five framework packages and managed symbols to NuGet.org. The reference plugins
  remain local examples. Install [NativeChannels.Maui](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.1)
  to resolve the framework dependencies for your platform.

See the [roadmap](docs/roadmap.md) for planned work and the
[verification record](docs/verification.md) for compiled targets, executed scenarios, and remaining checks.
