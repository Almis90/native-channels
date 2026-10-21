# Uno 0.3 samples

Five separate package-only Uno apps share startup, window shutdown and sample UI
code in `Shared`. They pin Uno SDK 6.7.30 and the local NativeChannels
`0.3.0-preview.6` packages. No MAUI or Avalonia host package is required.

| App | Interactive behavior | Automated Windows checks |
| --- | --- | --- |
| Interop | Host lifecycle checks; two Windows native buttons with reconnect and removal controls | Managed channel/lifecycle checks plus real HWND parenting, independent bounds/resize, focus, direct mouse/keyboard messages, reconnect and cleanup |
| Currency | Enter an ISO code and query its native symbol | EUR, USD, unknown-code error |
| AudioStreamer | Play the original shared music loop; pause, resume and stop | Silent PCM write, native pause/resume, disposal |
| VolumeMonitor | Start/stop observing the output volume without changing it | Initial native reading and subscription disposal |
| PdfViewer | Open local PDFs in a native Windows view; page, zoom, pan, reconnect and close | Real Windows.Data.Pdf page pixels, page/zoom changes, invalid input, reconnect and pending-request cleanup |

The PDF app renders through Windows.Data.Pdf on Windows, PDFKit on iOS, and
PdfRenderer on Android. The separate protocol-fixture button tests managed
contracts only. Interop uses the shared native method/view verifiers on iOS and
Android; its extra Windows controls remain desktop diagnostics. Successful
channel connectivity alone does not establish native rendering or physical input.

## Build and run

The unified runner supports Uno with an isolated feed and fresh local package
cache for each invocation:

```powershell
python eng/test-samples.py --framework uno --platform windows --dotnet .tools/dotnet/dotnet.exe --reuse-external-cache artifacts/uno-feasibility/cache
```

It packs all seven desktop packages from this checkout, audits their source
commit/metadata, builds the selected apps, rejects project references and
unrelated UI host dependencies, and checks the origin and bytes of every restored
NativeChannels/plugin package. `--reuse-external-cache` reuses only third-party
dependencies; local packages are always restored into a new cache. Use
`--sample Currency` to select one app; the default is all five. Results, source
identity, package hashes, resolved assets and logs are saved under
`artifacts/local-samples/uno/<platform>/<run>/` even on failure.

Use `--build-only` on Windows, Linux or macOS to compile on that OS. Only Windows
has a smoke execution path at present. Requests for Android, iOS or browser
produce blocked evidence and exit 2; Linux/macOS runtime requests also exit 2.
Build-only success records smoke as `not-run`. Smoke success still records full
acceptance as `blocked`, including for Interop and PDF. `--require-acceptance`
returns a failing exit code until full native-view/runtime acceptance exists.
No unsupported platform is silently skipped or reported as passed.

The `Uno / Desktop packages and sample builds` workflow runs the package audit
and five builds on Windows, Linux and macOS. Its uploaded evidence is compilation
evidence, not a release gate for six-platform support. Runtime execution on the
other platforms and automated native-view/PDF acceptance remain open step-6 work.

The earlier diagnostic scripts remain available for step-4/5 comparisons:

From the repository root on Windows, first build the local packages:

```powershell
pwsh -File eng/verify-uno-feasibility.ps1 -PluginProbe -ReuseCache artifacts/uno-feasibility/cache
pwsh -File eng/verify-uno-samples.ps1 -ReuseCache artifacts/uno-feasibility/cache
```

Omit `-ReuseCache` for a fresh NuGet cache. The feasibility script also executes
its existing plugin probe. The samples runner builds all five desktop apps,
runs each in the Uno event loop on Windows, and saves JSON outcomes, build logs,
resolved package assets and package hashes under `artifacts/uno-samples/`.
`-BuildOnly` builds without launching apps. Checks never start audible playback.

To open an interactive sample after building:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
& samples/Uno/Currency/bin/Release/net10.0-desktop/Currency.exe
```

Replace `Currency` with another app name. Audio only plays after pressing Play.
It uses the same `MusicGenerator.cs` as the MAUI sample. Closing a window awaits
cleanup; suspension/background entry stops playback and volume observation.
Foreground re-entry requires explicitly starting them again.

The Windows desktop heads use Uno's software renderer. On Uno 6.7.135, an early
exit can block joining the OpenGL render thread while it is flushing a frame.
Software rendering avoids that shutdown path and applies to interactive samples
as well as automated runs; these checks do not validate GPU rendering performance.
The PDF child HWND still uses its native Windows.Data.Pdf/GDI renderer.

## Acceptance still pending

The separate iOS runner builds fresh Apple packages and executes the
same shared verifiers used by MAUI/Avalonia on an ARM64 iPhone simulator:

```sh
python3 eng/verify-uno-ios.py --sample Currency --dotnet /absolute/path/to/dotnet --device SIMULATOR_UDID --reuse-external-cache ~/.nuget/packages
```

Run from the repository root on an ARM64 Mac with Xcode and the iOS workload.
Repeat with `--sample Interop`, `--sample AudioStreamer`, `--sample VolumeMonitor`
or `--sample PdfViewer`. The optional
cache argument reuses only external dependencies; local packages are fresh and
their restored bytes/source are checked. Normal simulator code signing stays
enabled. Results, package hashes and logs are under `artifacts/uno-ios/<run>/`.
All five passed on the iOS 26.5 simulator on October 21, 2026. These are Debug
simulator checks, not physical-device or release-candidate acceptance. Audio is
silent; hardware volume changes and actual OS background transitions were not
tested. Interop links the existing MAUI native verifier and Avalonia view verifier
with unchanged assertions, using the shared Apple view adapter with Uno's
dispatcher and UIKit embedding. Its native fixture is built from the same Swift
sample as MAUI/Avalonia. The runner requires both verifier completion markers,
including 32 reconnects. PdfViewer reuses the existing PDFKit backend and links
MAUI's PDF verifier unchanged apart from framework aliases. It checks document
loading, navigation, zoom, UI callbacks, independent viewers, native errors,
detach/reconnect and pending-work cleanup. This does not establish physical
input or visual rendering acceptance.

The Android runner builds the existing Kotlin adapters and fresh local packages,
then runs the same shared verifiers on one explicit device or emulator:

```powershell
python eng/verify-uno-android.py --sample Currency --dotnet .tools/dotnet/dotnet.exe --device emulator-5554 --android-sdk .tools/android-sdk --java-sdk .tools/android-jdk/jdk-21.0.12.1+1 --reuse-external-cache /absolute/path/to/nuget/packages
```

Repeat with `Interop`, `AudioStreamer`, `VolumeMonitor` and `PdfViewer`. The
samples require API 24, except AudioStreamer which requires API 31, matching
the existing MAUI/Avalonia adapters. The
runner selects ARM64 or x64 from the device ABI, embeds the managed assemblies
in the Debug APK, and enables verification only for that build. It records
package/APK hashes, restored package provenance, results and process logs under
`artifacts/uno-android/<run>/`. Interactive builds do not auto-run verification.

All five passed on the API 36 x64 emulator on October 21, 2026. Interop runs the
existing native method/message/event suite and native-view suite, including 32
reconnects. PDF runs the same shared document/navigation/zoom/lifecycle checks
as iOS. Currency, AudioStreamer and VolumeMonitor keep their existing shared
verifiers. No additional scenarios or Uno source patch were needed. These are
Debug emulator results; ARM64/physical-device input, audible playback, hardware
volume changes, visual acceptance and release packaging remain unverified.

The projects include Android, iOS, browser and desktop heads (desktop covers
Windows, Linux and macOS). Those heads are not a six-platform support claim.
The local preview feed built above contains desktop assets; mobile/browser
packaging and runtime checks remain necessary before those targets are usable.
macOS needs the native dylibs and has no public arbitrary native-view embedding
API in the pinned Uno version. Linux needs its native plugin assets.

Step 5 remains partial until native PDF adapters and native views work on all targets,
and all five apps have run on all six platforms. Windows native parenting,
bounds/resize, focus, direct input-message dispatch and two-view independence now
pass automated checks. The adapter supplies `WS_CHILD` before Uno reparents the
HWND; leaving `WS_POPUP` set caused the earlier failure. Automated runs are hidden:
physical input and visual appearance still need interactive review. The
protocol/host checks link the same fixtures used by the
feasibility consumer so their results can be compared without duplicated tests.
