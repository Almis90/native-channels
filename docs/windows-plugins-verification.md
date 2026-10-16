# Windows plugin verification

The Windows reference plugins implement Currency lookups, waveOut PCM playback, endpoint volume
notifications, and Windows.Data.Pdf rendering. The current preview's combined-package acceptance
requires the native API tests and application checks below.

| Plugin | Check |
| --- | --- |
| Currency | Native symbols, normalization, unsupported codes, concurrency, and cancellation |
| AudioStreamer | Silent PCM progress, pause/resume, bounded buffering, cancellation, drain, and restart; audible playback separately |
| VolumeMonitor | Initial endpoint level, independent subscriptions, changes, Stop/Start, and cleanup |
| PdfViewer | Loading, page/zoom commands, callbacks, independent views, errors, and reconnect |

On an interactive Windows desktop, run `./eng/verify-windows.ps1 -Plugin NAME` for the selected plugin.
Use `-PackageFeed artifacts/packages` to consume an existing combined feed without repacking. Omit
`-BuildOnly` for runtime verification; hosted build checks cannot establish interactive UI execution.

## Physical Windows execution (2026-10-17)

The supplied Windows verification report records a clean checkout at
`f9c779f57396609fdd931425eea4dacbd77cbfc2` on Windows 11 Home 25H2, build 26200.9711,
x64. All applications ran as Release, self-contained, unpackaged applications. The SDK and workload
set were 10.0.401, with MAUI Controls 10.0.110.

- 98 portable tests and 27 Windows plugin tests passed.
- All five application runtime checks passed, including the framework's 32 native-view reconnects.
- Native audio tests covered buffering, pause/resume, cancellation, disposal, drain and restart.
- Native volume tests delivered endpoint changes to two independent listeners and restored the
  original volume. PDF tests rendered three pages and checked errors, cancellation and file release.
- The user confirmed the four example checklists, including audible playback, live volume changes,
  volume restart, PDF navigation/zoom/reopen and window resizing. Interaction used Parsec; these
  observations are recorded separately from automated results. Manual framework interaction was skipped.

These consumers used **local Windows-only packages**, built at the recorded commit and reused across
the examples. There were no project references or mobile bindings; restored first-party archive
hashes matched the feed. This is not runtime evidence for the combined distribution feed.

Windows ARM64 execution, MSIX installation/activation/removal, other Windows versions, Store
distribution, extended-duration reliability and additional hardware configurations remain unverified.
The optional output-device-switch step received only a general checklist confirmation; separate
route details are needed before treating it as evidence for a particular device switch.

## Deployment and architecture

Record Windows version/build, x64 or ARM64, package hashes, exact source commit, and whether the app
was unpackaged or installed. Use `-Msix` with an explicitly selected signing certificate for separate
installation, activation, runtime, and removal checks. Do not count unsigned compilation as deployment.

The acceptance matrix includes Windows 11, Windows 10 22H2, and the declared minimum Windows 10
build 17763. ARM64 needs execution on ARM64 hardware. Bluetooth/headphone routes, audio interruptions,
volume endpoint changes, long-running native heap behavior, and Store distribution require additional tests.

See [Windows integration](windows-integration.md#combined-platform-packages) for commands and
[platform verification](verification.md#windows) for recorded current-preview results.
