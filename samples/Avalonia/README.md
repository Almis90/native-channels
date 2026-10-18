# Avalonia reference samples for 0.2

All four reference samples and the [interoperability sample](Interop/README.md) now have implementation heads for the six Avalonia
targets. This matrix describes source coverage, **not runtime verification**.
Android and Apple heads still require their platform execution pass.

For local automated execution of all five samples, use
`python eng/test-samples.py --platform windows` (or `linux`, `android`, `macos`,
`ios`, `browser`). See [local sample tests](../../docs/local-sample-tests.md) for
requirements, saved reports, and coverage limits. No workflows are invoked.

Local automation has executed all five Windows samples, all five browser samples,
and all six Linux heads, including both PDF backends. Linux VM verification uses Xvfb/Openbox and a temporary
PulseAudio null sink; this does not establish audible output. Local package feeds
pass metadata/platform audits and consumers are checked against their archives.
Builds use the pinned .NET SDK 10.0.401 and may reuse external dependency caches;
this is not fresh-cache release acceptance. Android, iOS and macOS execution remains
pending. No workflows are invoked by the local runner.

| Sample | Android | iOS | Windows | macOS | Linux | Browser |
|---|---|---|---|---|---|---|
| [Interop](Interop/README.md) | Kotlin / Android buttons | Swift / UIKit | Win32 buttons | Swift / AppKit | C++ / GTK buttons | JavaScript / DOM buttons |
| Currency | Kotlin | Swift | Windows globalization | Swift | C++ / ICU | JavaScript / Intl |
| AudioStreamer | Kotlin / AudioTrack | Swift / AVAudioEngine | WASAPI | Swift / AVAudioEngine | C++ / ALSA | JavaScript / Web Audio |
| VolumeMonitor | Kotlin | Swift | Core Audio | Swift / Core Audio | C++ / PulseAudio | Explicit unsupported system-volume result; separate page-volume event example |
| PdfViewer | Kotlin / PdfRenderer | Swift / PDFKit | Win32 / Windows.Data.Pdf | Swift / PDFKit | **Two apps: C++ / Qt PDF and C++ / GTK + Poppler** | JavaScript / PDF.js canvas |

The Avalonia application UI and shared host are C#. Native plugin implementations
demonstrate the platform's own APIs and languages. Linux system and toolkit APIs
are implemented in C++, behind a generic language-neutral C ABI.

Audio and volume use `Plugin.Maui.AudioStreamer` and `Plugin.Maui.VolumeMonitor`:
those package names are retained, but their service APIs do not require MAUI.
PDF uses `Plugin.Avalonia.PdfViewer` because its embedded control is host-specific.
The original MAUI PDF package and samples remain available separately.

The interoperability sample links the MAUI contract checks and adds Avalonia native-view
lifecycle checks. Build it separately with `bash eng/build-interop-avalonia.sh <target>`;
its checks run only when explicitly started in the app.

## Build all four reference-plugin samples for a target

Install the pinned SDK with `bash eng/bootstrap.sh` and the platform's .NET
workload/SDK prerequisites. Then run one of:

```sh
bash eng/build-reference-avalonia.sh android
bash eng/build-reference-avalonia.sh ios
bash eng/build-reference-avalonia.sh windows
bash eng/build-reference-avalonia.sh macos
bash eng/build-reference-avalonia.sh linux
bash eng/build-reference-avalonia.sh browser
```

The script builds native assets, packs a platform-scoped development feed, and
builds package-consuming heads. It never launches apps or workflows. Apple builds
require macOS/Xcode; Android requires its SDK/JDK; Windows desktop runtime
verification requires Windows. Browser requires the WebAssembly workload and
Node/npm. Feeds and consumer caches are isolated under
`artifacts/reference-avalonia/<target>`; preserve/remove the previous generated
target directory before rebuilding so identical package versions cannot go stale.

On Ubuntu 24.04 x64 install the native build dependencies:

```sh
sudo apt-get install g++ pkg-config libicu-dev libasound2-dev libpulse-dev qt6-base-dev qt6-pdf-dev libgtk-3-dev libpoppler-glib-dev
```

Linux PDF needs X11 (or XWayland); the Qt and GTK executables each select one
toolkit. PulseAudio's client API also works with PipeWire's PulseAudio service.
Volume monitoring reads the default output level every 150 ms and emits initial
and changed values; it never changes system volume.

## What the samples exercise

- AudioStreamer: explicit user-initiated playback, looping, pause/resume, bounded
  PCM streaming, progress and lifecycle cancellation. Browser requires user
  activation. The signal-level slider changes generated samples, not OS volume.
- VolumeMonitor: independent event subscriptions and cancellation. Browser APIs
  cannot observe system output volume; its separate slider demonstrates events
  from a page-owned media element and is labeled accordingly.
- PdfViewer: two isolated native instances, local file selection, page/zoom/state
  commands, detach/reconnect and disposal. Browser accepts PDFs up to 8 MiB and
  packages PDF.js locally. Reconnection creates an empty view with a new identity.

Prior Currency and Windows/Linux audio verification is recorded in the existing
sample documentation. Do not treat those results as verification of the new
heads, PDF backends, browser event support or macOS adapters. Device/browser
interaction, toolkit embedding, suspend/resume and teardown remain the next
runtime verification work.
