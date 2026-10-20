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

The PDF app now renders through Windows.Data.Pdf into a native Win32 child window.
The separate protocol-fixture button tests managed contracts only. Other platforms
do not yet have bundled PDF adapters.
Interop's native controls are Windows-only diagnostics; successful channel
connectivity is not evidence of native attachment, rendering or input.

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

## Acceptance still pending

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
