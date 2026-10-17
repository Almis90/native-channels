# Changelog

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
