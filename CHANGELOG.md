# Changelog

## 0.3.0-preview.1 � 2026-10-23

- Added optional, thin `NativeChannels.Uno` integration alongside MAUI and Avalonia,
  with owned sessions, UI dispatch, asynchronous shutdown and native view hosting.
- Added Uno samples for Android, iOS, Windows, macOS, Linux and browser/WebAssembly,
  reusing the existing sample verification scenarios.
- Added a macOS desktop transport and a temporary native view hosting bridge pending
  [Uno #25639](https://github.com/unoplatform/uno/issues/25639).
- Fixed Windows native view painting, focus and PDF pointer routing, reentrant
  shutdown, and Avalonia native view cleanup ordering during reconnect.
- Aligned sample interactions with MAUI, including safe areas and PDF controls.
- Release scope is the six existing framework packages plus `NativeChannels.Uno`.
  Reference plugin packages are excluded. NuGet publication is still pending;
  prior diagnostic package checks do not validate the final release archives.
- See [0.3 preparation](docs/preview-03.md) for platform evidence and limitations.

## 0.2.0-preview.1 — 2026-10-19

- Renamed the framework-independent reference packages and C# namespaces from
  `Plugin.Maui.Currency`, `Plugin.Maui.AudioStreamer`, and `Plugin.Maui.VolumeMonitor`
  to `Plugin.Currency`, `Plugin.AudioStreamer`, and `Plugin.VolumeMonitor`.
  Update package references and `using` directives, then rebuild local plugin feeds.
  Browser assets now use the new package IDs under `_content/`. Existing native channel
  identifiers and Apple framework names are preserved for protocol compatibility.
  The MAUI and Avalonia integration packages remain optional; PDF view plugins retain
  their framework-specific names.
- Framework-independent registration and diagnostics, with a reusable Avalonia host for
  owned sessions, UI dispatch, and native/DOM view lifetimes.
- Avalonia implementations of ChannelExplorer/Interop, Currency, AudioStreamer, VolumeMonitor,
  and PdfViewer for Android, iOS, Windows, macOS, Linux, and browser/WebAssembly.
- Linux native adapters use C++; PDF examples provide separate Qt and GTK backends.
- Local automation has passed all five samples on all six Avalonia targets and all
  four MAUI targets; Linux includes separate Qt and GTK PDF results.
- Local preview preparation records clean-source package hashes, combines the six
  framework packages, and requires exact-package API and consumer results.
- Combined candidate `4f1079b` passed local API/package acceptance and all ten
  sample suites (51 executions); all tests ran locally.
- Published all six framework packages and managed symbols through NuGet Trusted Publishing.
  Only the publishing workflow ran. Public archive contents match the accepted candidate;
  fresh NuGet.org restores and local Windows MAUI/Avalonia Interop checks passed.
- Preserved the portable Linux API across platform assemblies and corrected
  dependency resolution in API checks and Apple Interop build prerequisites.
- Physical devices, additional architectures, audio audibility, PDF visual fidelity
  and broader lifecycle checks remain separate coverage; see `docs/preview-02.md`.

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
