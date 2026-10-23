# Uno 0.3 preview

Status on October 23, 2026: **0.3 preview source release; NuGet publication pending**.
The optional Uno host and five samples are implemented. Shared automated sample
checks passed on all six platforms against a previous combined diagnostic feed.
Manual review covered macOS, iOS, Android, Windows and Linux; browser manual review
is partial. Physical-device coverage remains separate.

## Package and version boundaries

The repository version is `0.3.0-preview.1`. Publication includes the six existing
framework packages plus `NativeChannels.Uno`; reference plugin packages are excluded.
The earlier `0.3.0-preview.6` feed was local diagnostic output, not a public release.
The release uses a separate seven-framework acceptance gate described in
[publishing](publishing.md#seven-package-03-preview). It verifies the exact final
archives with the existing Interop channel and native-view suites on all 16
supported framework/platform combinations, plus API compatibility against the
six published 0.2 frameworks. This scope excludes the reference plugin samples;
their earlier diagnostic-feed results are recorded below. NuGet publication is
still pending. The exact-archive gate passed on October 23, 2026; see the
[acceptance record](verification.md#accepted-03-preview).

| Package | Responsibility |
| --- | --- |
| `NativeChannels.Core` | Framework-independent messaging, codecs and channel lifetimes |
| `NativeChannels` | Platform transports and shared native module registration |
| `NativeChannels.Uno` | Optional Uno startup, per-window sessions, UI dispatch and experimental view lifecycle |
| `Plugin.Currency`, `Plugin.AudioStreamer`, `Plugin.VolumeMonitor` | Shared messaging plugins; no dependency on a UI host |
| `Plugin.Uno.PdfViewer` | Uno PDF control with Windows.Data.Pdf, iOS PDFKit, Android PdfRenderer, Linux X11 Qt/GTK and browser PDF.js backends; custom factories remain supported |

The desktop transport selects Windows, Linux or macOS at runtime. The macOS
plain-.NET bridge stays in the transport/native layer. It does not belong in
the Uno host. MAUI and Avalonia hosts remain optional; adding Uno does not require
renaming or duplicating the three shared messaging plugins.

Uno SDK 6.7.30 resolves runtime 6.7.135. The current samples use Skia and .NET SDK
10.0.401. See [host setup](uno-host.md), [plugin registration](uno-plugins.md),
and the [sample instructions](../samples/Uno/README.md).

## Current evidence

| Platform | Executed evidence | Still required |
| --- | --- | --- |
| Windows | Five package-only apps pass bounded smoke checks; real Currency, silent audio and volume plugins execute. Corrected button adapters pass native checks; Windows PDF painting, page/zoom, invalid-input and cleanup checks also pass | Visible review and physical input, broader PDF/document/lifecycle coverage and audible playback checks |
| macOS | ARM64 shared messaging checks plus Interop and PDF parity pass; native hosting uses the temporary adapter bridge on released Uno 6.7.135; x64 native libraries cross-compile | Public hosting API tracked in [Uno #25639](https://github.com/unoplatform/uno/issues/25639), x64 execution, physical input and manual acceptance |
| Linux | All five fresh-package apps pass shared verifiers on Ubuntu 24.04 x64 X11/Xvfb, including native Interop views and both Qt/GTK PDF backends; audio uses a PulseAudio null sink | Visual/physical-input acceptance, audible playback, hardware volume changes, other architectures/backends |
| Android | All five package-only apps pass existing MAUI/Avalonia shared verifiers on an API 36 x64 emulator, including native Interop views and PdfRenderer | ARM64/physical-device behavior and visual/input acceptance remain unverified |
| iOS | All five apps pass shared verifiers on an ARM64 iPhone simulator, including Interop native methods/UIKit views and PDFKit document/navigation/zoom/lifecycle checks | Physical-device behavior remains unverified |
| Browser | All five fresh-package apps pass existing shared browser verifiers in headless Chromium on Windows x64, including DOM button hosting and PDF.js; volume checks cover a page-owned audio element | Cross-browser and manual acceptance, audible playback |

The earlier local step-6 Windows run rebuilt all seven diagnostic packages,
audited their metadata and source commit, and verified that every local package
restored by each app matched that run's feed byte-for-byte. Local package caches
were fresh; third-party dependencies were reused. All five builds and smoke
checks passed. Interop uses managed loopback for its automated channel/lifecycle
checks; PDF uses a managed protocol fixture. Neither result proves native rendering.

That run used package source `b24c6d623e53d806f1c552c7be67208e12112518`
and the then-uncommitted runner subsequently committed as `319c672`. Its source
digest and dirty-tree status are recorded in
`artifacts/local-samples/uno/windows/20261019-232553-2e12f0f0/summary.json`.
These local artifacts are not a hosted CI result or clean release-candidate proof.
The runner tests passed 52 checks; workflow lint and documentation validation
also passed during step 6. Earlier probe scope is recorded in
[Uno feasibility](uno-feasibility.md).

## Reproduce and interpret results

On Windows, from the repository root:

```powershell
python eng/test-samples.py --framework uno --platform windows --dotnet .tools/dotnet/dotnet.exe
```

Use `--reuse-external-cache artifacts/uno-feasibility/cache` to reuse third-party
dependencies while retaining a fresh cache for local packages. Use `--require-clean`
when collecting candidate evidence. `--sample Currency` selects one app; omit it
for all five. Each invocation writes its own package feed, hashes, restore assets,
build/runtime logs, source identity and summary under `artifacts/local-samples/uno`.

`--build-only` compiles on Windows, Linux or macOS and records runtime checks as
`not-run`. The desktop CI workflow defines these three build jobs; they have not
been executed as part of the local work recorded here. CI configuration is not
evidence that those jobs passed.

Only Windows has a smoke execution path in the unified Uno runner. Unsupported
runtime requests produce blocked evidence and exit 2. Even successful Windows
smoke checks record full acceptance as `blocked`. Add `--require-acceptance` to
make incomplete acceptance fail the command. A zero smoke/build exit code must
not be used as a release approval.

The October 21 blank-content failure was isolated to parent painting over native
child HWNDs. The Uno host now enables child clipping on the actual parent and
requests a repaint. All five fresh-package smoke checks pass; desktop-tool review
confirms stable buttons and PDF pages, navigation, zoom and reconnect. PDF
panning now passes native input tests and desktop-tool review. The shared sample
close handler now queues cleanup before its final close request, fixing the PDF
window-close hang; smoke tests exercise normal window close. Broader acceptance
remains open. An intermittent Interop clipping
assertion led to a bounded stable-readiness check with timeout diagnostics;
the original failure's cause has not been conclusively reproduced. See
[visible review findings](uno-feasibility.md#windows-visible-review-october-21-2026).

## Preview limitations and earlier release preparation

- Complete visible Windows review and physical input verification. The corrected
  adapter passes automated two-view parenting, layout, focus, direct input messages,
  reconnect, stale channel invalidation and cleanup checks; see the
  [Windows probe correction](uno-feasibility.md#windows-native-view-probe).
  The follow-up fixes root-HWND Tab/Shift+Tab focus trapping; PDF document text
  and reliable accessibility focus reporting remain outstanding. See the
  [keyboard and accessibility review](uno-feasibility.md#windows-keyboard-and-accessibility-review-october-21-2026).
- The accepted temporary macOS hosting bridge works with released Uno 6.7.135;
  replace it when the public embedding API tracked in
  [Uno #25639](https://github.com/unoplatform/uno/issues/25639) is available.
  The source retains a TODO pointing to that issue. No Uno source patch is required.
- The complete local 13-package candidate now passes all five existing sample
  verifiers across all six platforms, including both Linux PDF backends. This
  closes the local combined-packaging and automated-candidate gates; it does not
  establish physical-device, additional-architecture or manual acceptance.
- Extend the hosted distribution/publication workflow for Uno. Local packaging
  now preserves MAUI/Avalonia targets and passes the existing API compatibility
  gate; the exact combined archives were tested from clean, unchanged source. The
  earlier 0.1/0.2 publication path is now supplemented by the separate seven-package
  0.3 acceptance path above. The original 0.2 gate remains unchanged.
- After acceptance, update release versions and dependency pins together, finalize
  release notes, and follow the [release checklist](release-checklist.md). Publication
  and public-feed consumer checks are separate steps.

Documentation and release notes are prepared here; they do not close the earlier
milestone gates. The full 0.3 milestone remains incomplete.


### Combined desktop PDF preparation

The desktop PDF package now compiles Windows, Linux and macOS adapters into one
`lib/net10.0/Plugin.Uno.PdfViewer.dll` and selects the backend at runtime. Platform
packaging flags select native assets, not different managed implementations at the
same NuGet path. Mobile/browser builds continue to exclude desktop backends.

Validation on October 22, 2026:

- Windows fresh-package PDF build and existing native checks passed in
  `artifacts/local-samples/uno/windows/20261022-110457-da8a2c20/`.
- Mac ARM64 fresh-package Interop and PDF shared verifiers passed in
  `artifacts/uno-macos-views/136b1fa2c03b4bc3aab40b01c1d4c9a4/`.
- The exact Windows-built PDF DLL also passed the shared PDF verifier on the Mac.
  SHA-256: `4834ff12f5b6918b32ec775927fb3421de0690a4e4f63d6ddad4f3e749cb5db4`.
- Package-scope tests verify identical desktop managed inputs and keep desktop
  backends/dependencies out of mobile/browser builds.

Linux execution subsequently passed on the recovered VM, including the existing Qt
and GTK PDF checks. The first combined Uno candidate also passed all five Windows,
macOS ARM64 and Linux samples, plus browser Interop and PDF. Browser Currency,
AudioStreamer and VolumeMonitor, and the combined Android/iOS runs remain open.
This is not a completed six-platform candidate. No package has been published.

### Combined iOS PDF asset selection

The first combined candidate exposed an Uno 6.7.135 build behavior: NuGet selected
the iOS PDF assembly, but `ReplaceUnoRuntime` replaced it with the portable desktop
assembly. The simulator then timed out waiting for its native PDF view. The package
targets now retain NuGet's compile/runtime selection for this package on Skia iOS
and Android, preserving metadata and leaving Uno's own runtime selection intact.

Four MSBuild regression tests cover iOS, Android, desktop and the native mobile
runtime. On October 22, a diagnostic run using the corrected targets and a clean
rebuild on the Mac passed the existing shared PDF verifier: document, navigation,
zoom, callback, isolation, errors and detach/reconnect. The deployed assembly matched
the candidate's iOS DLL (`fb9f50016f4c49bb40e77941127d938657f5c688bc4cced63c01d9cdd4abac62`).
Evidence was copied to `artifacts/ios-mobile-pdf-fix/`. No Uno source patch was used.

The iOS runner now rebuilds and verifies the deployed PDF assembly against the
package, because checking restored archives alone cannot detect Uno's later asset
replacement. The diagnostic modified an extracted package target, not the candidate
archive: regenerate and test the final combined packages before acceptance. Android
runtime confirmation of this correction is still pending. Existing MAUI/Avalonia
package target frameworks must also be retained before treating the feed as a full
0.3 release candidate.

### Reconnect failures found by combined-package checks

The existing Android PDF verifier exposed a bridge shutdown race: Kotlin's
`Collection.toList()` can read a size of one and then call `next()` after the last
reply has concurrently removed itself. The bridge now uses the concurrent map's
array snapshot. A deterministic regression test reproduces the original
`NoSuchElementException`; all 15 Android bridge unit tests pass with the correction.
The combined-package Android PDF runtime check also passed with this fix.

Repeated runs of the existing iOS PDF verifier exposed a separate reconnect timing
failure. A newly created PDFKit view can open a document before it has nonzero
bounds, leaving its native scale at zero. The shared Apple adapter now initializes
a valid native scale while waiting for its resize callback to calculate the actual
size-to-fit scale. The previous build failed twice in four runs; a diagnostic build
with the correction passed five consecutive runs of the same shared verifier.
Evidence is under `artifacts/uno-candidate-shutdown/`. These corrections do not add
sample scenarios or require Uno source changes. Final archive validation remains
separate from these diagnostic runs.

### Combined candidate validation: October 22, 2026

The nine-package Uno feed in `artifacts/uno-candidate-apple-fit/packages/` was
assembled from source commit `3c76090ba651d5554bdd28ffb8ff3d6fe7fc31b6`. The existing
Interop, Currency, AudioStreamer, VolumeMonitor and PDF checks all passed on:

- Windows desktop.
- macOS ARM64 desktop, using the temporary hosting bridge.
- iOS ARM64 simulator.
- Android x64 emulator.
- Browser/WebAssembly.

The mobile/browser runners verified restored package hashes against the same
archives. The iOS PDF check additionally verified the deployed assembly hash.
Windows used clean, unchanged source and checked the packaged Windows projections;
macOS checked the restored package hashes before execution. No Uno source patch or
additional sample scenario was used. These are automated sample results, not
physical-device, listening or manual acceptance.

The consolidated evidence is `artifacts/uno-candidate-apple-fit/acceptance-status.json`.
The Linux VM initially stopped answering SSH. After it recovered, all five
samples and both Qt/GTK PDF backends passed against these exact archives. Evidence
is in `artifacts/uno-candidate-apple-fit/linux-evidence/`. The subsequent full
candidate below rebuilds the Linux inputs on the VM instead of reusing old binaries.

This earlier nine-package feed omitted existing MAUI/Avalonia targets and pulled
unused Linux libraries into Android APKs. Those issues are resolved in the full
candidate below. Manual acceptance and release steps remain separate.

### Preserve MAUI and Avalonia targets in a release candidate

`eng/pack-uno-candidate.py` packs one input into a new directory using already-built
native assets. Use `--include-existing-hosts` for a full framework candidate. Build
all six Uno inputs (`windows`, `linux`, `macos`, `android`, `ios`, `browser`) and the
three compatibility inputs (`windows-native`, `maccatalyst`, `macos-native`) at the
same clean commit and package version. The native Windows input uses the existing
Windows-specific TFM; `macos` remains Uno's portable Skia desktop input.

On the Mac, build the generic bridge and each plugin's Apple adapter with
`--include-macos` before packing any Apple inputs. This creates a single
XCFramework containing iOS, Mac Catalyst and macOS slices, avoiding conflicting
platform-specific XCFramework manifests when packages are merged.

Pass the nine input feeds to `eng/combine-uno-packages.py --include-existing-hosts`.
The assembler requires all 13 packages, including the optional MAUI/Avalonia hosts
and PDF controls, and rejects missing existing consumer targets. Omitting this
flag still produces the nine-package Uno development feed; it is not the complete
framework release. Run the existing consumer checks against the resulting feed
before publication. The audit continues to mark runtime acceptance as pending.


### Full compatible candidate: October 22, 2026

`artifacts/uno-candidate-compatible/packages/` contains 13 packages assembled from
nine platform inputs at clean source commit
`a17d661575c43f6f8d12ee8513b63203c7b1b5b3`. It retains the optional MAUI/Avalonia
hosts and PDF controls, along with the existing Android, iOS, Mac Catalyst,
native macOS, Windows-specific, portable desktop and browser targets.

All five existing Interop, Currency, AudioStreamer, VolumeMonitor and PDF sample
verifiers pass against these same archives on Windows, macOS ARM64, Linux x64 X11,
Android x64 emulator, iOS ARM64 simulator and browser Chromium. Both Linux Qt and
GTK PDF backends pass. No sample scenarios were added. Evidence, package hashes
and source identities are consolidated in
`artifacts/uno-candidate-compatible/acceptance-status.json`.

Android packages now provide an explicit empty native asset group, preventing
NuGet's Android-to-Linux fallback from selecting glibc libraries. Restore regression
checks cover all four Android RIDs and confirm Linux still receives its libraries.
All five built Android APKs were inspected and exclude the Linux plugin libraries;
see `android-apk-audit.json` beside the candidate.

The full package API check exposed a portable contract mismatch: `WindowsFactory`
and `IWindowsChannelModule` existed in `net10.0` but were missing from its mobile
specializations. They are now available on every target, like `LinuxFactory`, and
are still used only by the Windows transport. The existing API compatibility gate
passes for all five hash-pinned framework baselines at `0.1.0-preview.1`. Run it for
this local version with:

```powershell
python eng/verify-api-compatibility.py --packages artifacts/uno-candidate-compatible/packages --version 0.3.0-preview.6 --dotnet .tools/dotnet/dotnet.exe
```

The API tool still emits unresolved external platform-reference warnings; its
baseline coverage is the five packages in `eng/api-baseline.json`. These local
results do not claim physical-device, listening, accessibility or manual input
acceptance. The hosted 0.3 distribution/publication workflow, release version
approval and existing manual release checklist remain open. No push, release tag
or publication was performed.
