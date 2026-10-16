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

## Deployment and architecture

Record Windows version/build, x64 or ARM64, package hashes, exact source commit, and whether the app
was unpackaged or installed. Use `-Msix` with an explicitly selected signing certificate for separate
installation, activation, runtime, and removal checks. Do not count unsigned compilation as deployment.

The acceptance matrix includes Windows 11, Windows 10 22H2, and the declared minimum Windows 10
build 17763. ARM64 needs execution on ARM64 hardware. Bluetooth/headphone routes, audio interruptions,
volume endpoint changes, long-running native heap behavior, and Store distribution require additional tests.

See [Windows integration](windows-integration.md#combined-platform-packages) for commands and
[platform verification](verification.md#windows) for recorded current-preview results.
