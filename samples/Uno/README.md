# Uno 0.3 samples

Five separate package-only Uno apps share startup, window shutdown and sample UI
code in `Shared`. They pin Uno SDK 6.7.30 and the local NativeChannels
`0.3.0-preview.6` packages. No MAUI or Avalonia host package is required.

| App | Interactive behavior | Automated Windows checks |
| --- | --- | --- |
| Interop | Host lifecycle checks; two Windows native buttons with reconnect and removal controls | Managed method/message echo, events, dispatch, cancellation, isolated windows, reconnect and stale channels |
| Currency | Enter an ISO code and query its native symbol | EUR, USD, unknown-code error |
| AudioStreamer | Play the original shared music loop; pause, resume and stop | Silent PCM write, native pause/resume, disposal |
| VolumeMonitor | Start/stop observing the output volume without changing it | Initial native reading and subscription disposal |
| PdfViewer | Run the experimental control's protocol checks | Page, zoom, callbacks, identity, reconnect and disposal using a managed fixture |

**The PDF app does not render a document.** No Uno native PDF adapter is bundled.
Interop's native controls are Windows-only diagnostics; successful channel
connectivity is not evidence of native attachment, rendering or input.

## Build and run

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

Step 5 remains partial until native PDF adapters and native views are working,
and all five apps have run on all six platforms. Windows native attachment,
input/focus, visibility, two-view independence and resize must still be tested
interactively. The Interop app exposes controls for this but does not mark these
checks passed. The protocol/host checks link the same fixtures used by the
feasibility consumer so their results can be compared without duplicated tests.
