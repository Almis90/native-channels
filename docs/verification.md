# Platform verification

The current 0.2 [Avalonia reference sample matrix](../samples/Avalonia/README.md)
adds AudioStreamer, VolumeMonitor and PdfViewer implementation heads for all six
targets, including separate C++ Qt/GTK Linux PDF apps. Local execution of all five
samples on Windows, Android, Linux, browser and Apple targets is recorded below,
with package audits and source provenance. The October 19 combined-package
candidate passed local acceptance below; publication and broader platform
coverage remain separate.

This record separates implementation, compilation, runtime checks, and manual observations.
Package acceptance requires verification of the exact source revision and generated archives.

| Status | Meaning |
| --- | --- |
| Planned | Roadmap scope; not implemented or verified support on `main` |
| Implemented | Code exists for the target |
| Compiled | The specified configuration built successfully |
| Executed | The scenario ran on the stated environment |
| Unverified | The scenario has not been checked on that environment |

The published 0.1 support record covers MAUI. The unreleased 0.2 candidate adds
Avalonia on six targets and has passed local combined-package verification. Uno
remains [planned](roadmap.md) for 0.3. Historical Currency proofs below establish
only their recorded scope; the newer complete suites do not establish publication,
physical-device support, or capabilities unavailable on an OS/browser.

## Local combined 0.2 preview candidate — 2026-10-19 {#local-02-candidate}

All ten suites passed against the same six framework packages and symbols built
from clean commit `4f1079baeea12e7708f81940d832e8ddc3d58aa0`: **51 sample executions**,
including both Linux PDF heads. The [machine-readable report](/verification/preview-02-local.json)
records archive hashes, the combined API result, clean build/execution identities,
summary hashes, per-sample input-manifest hashes and the acceptance receipt.

| Framework | Executed environment | Result | Evidence ID |
| --- | --- | --- | --- |
| Avalonia | Windows x64 | 5/5 PASS | `20261019-150945-6cd00232` |
| Avalonia | Android API 36 x86_64 emulator | 5/5 PASS | `20261019-152634-dd7a5008` |
| Avalonia | iOS simulator ARM64 | 5/5 PASS | `20261019-151239-9f940927` |
| Avalonia | macOS ARM64 | 5/5 PASS | `20261019-150956-65035cef` |
| Avalonia | Linux x64 / X11 | 6/6 PASS | `20261019-120958-7aa7d328` |
| Avalonia | Browser / Chromium | 5/5 PASS | `20261019-152613-5475446e` |
| MAUI | Windows x64 | 5/5 PASS | `20261019-151150-c7c68511` |
| MAUI | Android API 36 x86_64 emulator | 5/5 PASS | `20261019-155922-0b7aafcc` |
| MAUI | iOS simulator ARM64 | 5/5 PASS | `20261019-152544-01c3572d` |
| MAUI | Mac Catalyst ARM64 | 5/5 PASS | `20261019-152211-7a2cfe79` |

Windows used build 26200 and the API 36 Android emulator; browser checks used
Chrome 155.0.8059.80 through Playwright. Linux ran in the Ubuntu 24.04 x64 VM (kernel 6.8.0-142,
glibc 2.39), with Xvfb/Openbox and a temporary PulseAudio null sink. The Mac ran
macOS 26.2 ARM64, Xcode 26.6 (17F113), and an iPhone 17 Pro iOS 26.5 simulator.
All hosts used .NET SDK 10.0.401. External dependencies were reused through
hardlinked caches; framework/reference-plugin restores used isolated caches.

Package assembly checked all ten clean-source inputs, seven transport targets,
six package identities, managed symbols and browser assets. The five existing
package APIs passed comparison with the hash-pinned 0.1.0-preview.1 baseline;
Avalonia is new. Every consumer verified its resolved local archive hashes before
execution. The acceptance gate checked all ten summaries against the exact
candidate and API-report hashes. Full local evidence is under
`artifacts/preview-02-r3/`; Apple and Linux reports were copied to its `results/`
subdirectory. Original per-app logs and hashes remain with the suite evidence. The first
browser launch lacked Playwright's downloaded Chromium; the same built binaries
then passed using the installed Chrome channel, without rebuilding packages.
The first MAUI Android run timed out in AudioStreamer because a System UI ANR
dialog held window focus. After rebooting that emulator, the complete suite
passed with the same binaries. Both failed attempts remain in the local evidence.

Preparation fixed a real cross-target API mismatch: Linux APIs in the portable
assembly must also exist in the platform assemblies. Linux module construction
still requires Linux. The API checker now resolves same-feed framework dependencies
so inherited MAUI options are compared correctly. Negative checks still reject
the original Linux mismatch and a deliberately removed inherited option. Apple
consumer builds now refresh the Interop sample framework for the current target.
The final revision also passed 187 packaging regressions; 23 local runner/candidate
tests and 52 targeted transport/plugin tests passed during preparation.

This is local preview acceptance. The validation stage ran no workflows and
performed no tag, push or publication. The subsequent [local publisher](publishing.md)
can replay this evidence for six-package publication. Windows ARM64, Apple x64, physical phones, native Wayland, other
browser engines, signing/store deployment, accessibility and long native heap
soaks remain unverified here. Audio audibility, hardware volume changes and PDF
visual fidelity remain manual checks. See [preview scope](preview-02.md).

## Local Windows, Android, Linux and browser sample execution — 2026-10-18–19

The local sample runner passed the following suites. Windows execution used
Windows 11 x64 build 26200 and .NET SDK 10.0.401. Android execution used the
API 36 x86_64 emulator on that Windows host. Browser execution used Playwright
Chromium on Windows. Linux execution used the Ubuntu VM, x64 kernel 6.8.0-142,
glibc 2.39, Xvfb and a temporary PulseAudio null sink. No GitHub workflows ran.

| Framework | Target | Executed result | Local summary under `artifacts/local-samples/` |
| --- | --- | --- | --- |
| Avalonia | Windows x64 | 5/5 PASS | `windows/20261018-114801-84e7db3c/summary.json` |
| Avalonia | Android API 36 emulator | 5/5 PASS | `android/20261018-121658-ce6541da/summary.json` |
| Avalonia | Linux x64 VM | 6/6 PASS (both Qt and GTK PDF heads) | `linux-vm/20261018-084355-c233d06c/summary.json` |
| Avalonia | Browser / Chromium | 5/5 PASS | `browser/20261018-114635-7291d3f9/summary.json` |
| MAUI | Windows x64 | 5/5 PASS | `maui/windows/20261018-124704-a9954098/summary.json` |
| MAUI | Android API 36 emulator | 5/5 PASS | `maui/android/20261018-125339-f1cbf1ac/summary.json` |
| MAUI | Windows x64, updated runner with existing binaries | 5/5 PASS | `maui/windows/20261019-122754-38532347/summary.json` |

Each suite covers Currency, AudioStreamer, VolumeMonitor, PdfViewer and Interop
(ChannelExplorer in MAUI). Checks exercise native calls, playback state and
buffering, volume subscriptions, PDF state/navigation and native-view lifecycle,
plus the runner's completion and cleanup checks. MAUI has no Linux or browser
heads in this repository.

These are development working-tree builds, not clean builds of the current HEAD:

- Avalonia Windows and browser builds were based on `5c98b8856afb79ef438d6b8fb1e7fcaae638619c`
  with the local automation and sample fixes subsequently committed in `bee64e2`.
- Avalonia Android and both MAUI builds were based on `bee64e2cd20db899c120ef53e73155c6b4b04f0a`
  with local fixes subsequently committed in `593739c`. Their reports retain the
  distinct source hashes and dirty-tree records for each build.
- The Linux VM had a synchronized working tree over its older checkout at
  `9c1a15506cce74f06fb981f430e1b5f563857ef2`. Its recorded build source SHA-256 is
  `255cfe217232d45bbab68f4fb46acac64c2ddb28e714f5ec55dc6733138d5909`;
  the commit alone does not identify the tested source. The table points to the
  copied report on Windows; its manifest refers to the original Linux VM path.
- Browser, Linux and Avalonia Android execution reused their recorded build
  manifests. Reports distinguish build source from execution source; a later
  runner or source change does not rebuild those binaries.
- The October 19 Windows MAUI rerun used the runner at clean commit
  `9e515b0ec62f93d975deeeae4385ebc5c5055500` with the October 18 binaries.
  It verified combined stdout/stderr cleanup handling, not a fresh application
  build of the Apple commit. Ten runner tests and the evaluated PDF packaging
  scope test also passed on Windows; Windows-only PDF targeting was checked.

Builds used local package feeds, package audits and package-only consumer checks.
Reports and manifests retain source provenance; per-sample logs and input hashes
identify the executed outputs. Fixes included browser audio/native-view lifecycle,
Linux PDF handling, Android AppCompat hosting and application lifetime, and shared
MAUI verification/cleanup. Windows MAUI uses a short isolated package-cache path
to avoid WinUI compiler path-length failures.

Audio audibility remains a manual check; Linux null-sink execution does not prove
audible output. Visual PDF fidelity, hardware volume buttons, accessibility and
physical-device lifecycle behavior remain manual or unverified. Browser volume
checks concern page-owned audio, and its single-threaded runtime cannot exercise
background-thread rejection. These results do not establish released-platform
acceptance or a fresh cross-platform run of one common source revision.

## Local Apple sample execution — 2026-10-18–19

The `develop` working tree based on `593739c8d2e97709093e474da3f5b56aa27aa399`,
with the local Apple runner/sample fixes, passed all five samples sequentially on
macOS 26.2 ARM64. Tooling: .NET SDK/workload set 10.0.401, Xcode 26.6 (17F113),
and iPhone 17 Pro simulator running iOS 26.5. No GitHub workflows were invoked.

| Framework | Target | Executed result | Local summary under `artifacts/local-samples/` |
| --- | --- | --- | --- |
| Avalonia | macOS ARM64 | 5/5 PASS | `macos/20261018-151455-3e072b83/summary.json` |
| Avalonia | iOS simulator ARM64 | 5/5 PASS | `ios/20261018-153208-19d1b093/summary.json` |
| MAUI | Mac Catalyst ARM64 | 5/5 PASS | `maui/maccatalyst/20261018-155309-0d25b091/summary.json` |
| MAUI | iOS simulator ARM64 | 5/5 PASS | `maui/ios/20261019-024126-e8ca7719/summary.json` |

Each suite covers Currency, AudioStreamer, VolumeMonitor, PdfViewer, and Interop
(ChannelExplorer in MAUI), including cleanup. Packages were rebuilt and audited,
and consumers restored from fresh scoped caches with exact local package checks.
External dependencies were hardlinked between caches to conserve disk space.
The MAUI iOS final execution reused its fresh build manifest after host sleep and
maintenance-wake audio failures; the unchanged binaries passed with the Mac fully awake.
Each report records its own build source and execution source hashes.

Fixes cover AppKit button title assignment, runnable Apple bundle output, native
iOS launch argument detection, Apple-only MAUI PDF packaging, and Catalyst cleanup
markers written to stderr. Checks passed: ten runner contract tests, one evaluated
PDF packaging-scope test, 185 packaging regressions, changed-file formatting, and
the documentation build. Logs and environment details are retained in
`artifacts/local-samples/session-20261018/`. Earlier suites' app binaries were removed
to conserve disk space; their reports, hashes, package audits and manifests remain.

Audio audibility, visual PDF fidelity, hardware volume buttons, accessibility and
physical-device lifecycle behavior remain manual or unverified. No loopback audio
recording was added. Avalonia iOS builds emitted an IL2104 DesignerSupport trimming
warning; simulator logs also contain Apple accessibility duplicate-class warnings.
These local results do not change released-platform acceptance.

## Reusable Avalonia host foundation — 2026-10-17

The working tree based on `cde99a8b7616255e66ade57cde1d6adfcd870c38` adds
`NativeChannels.Avalonia`: portable AppBuilder registration, independent owned sessions,
awaited shutdown and asynchronous UI dispatch. All Currency heads now configure this host;
the shared UI consumes it through a NuGet package. Core remains independent of Avalonia.

| Check | Executed result |
| --- | --- |
| Avalonia 12.1.4 headless host tests | 12 passed: registration validation, provider isolation, 32 session recreation cycles, shared disposal completion, startup/cleanup failures, pending requests, final plugin stop requests, UI dispatch/cancellation and reentrancy |
| Existing portable / Linux regressions | 114 portable and 18 Linux tests passed |
| Packaging regressions | 168 passed, including required host archive provenance/hash checks |
| Linux x64, Ubuntu 24.04.5, X11/Xvfb + Openbox | Fresh package-only Release build had no warnings/errors; ICU Currency checks, verification exit and normal window close passed |
| Chrome 155.0.8059.80 / Firefox 157.0 on Windows 11 x64 | Fresh Release WebAssembly consumer passed Currency, binary/transport, actual Avalonia UI callback dispatch with a nested native request, session disposal/recreation, UI input and two loads with orderly disposal per engine |
| Windows 11 x64, build 26200 | Fresh package-only self-contained Release consumer, cross-built on Ubuntu with SDK 10.0.401, executed Currency checks and normal shutdown locally; exit code 0 and exactly one close marker |

Each consumer restored four local archives, including the new host. The host itself compiled
against the feed's Core archive in a fresh cache. Browser module bytes and the complete publish
output were checked; Windows published managed assemblies matched their package bytes.
Build/runtime logs, package hashes, source manifests and runtime records are retained under
`artifacts/avalonia-host`. Evidence is explicitly marked as a working-tree development build.
Documentation written after execution does not become part of an earlier source snapshot.

The current browser preview serves this host-based consumer. Workflow lint, source formatting
and the documentation build/link checks passed. No packages were published and no GitHub workflow
execution is claimed. The Windows check covered the Avalonia consumer; it did not repeat the MAUI
runtime proof. Android, iOS, Catalyst and native macOS runtime checks have not been repeated after
this host migration. Native/DOM views, broader suspension/resumption and multi-window acceptance,
events and the remaining plugins are still part of 0.2. See the [host guide](avalonia-host.md).

## Avalonia in the browser

Local verification on 2026-10-17 used source `4612d48323ce66123d3c1c94852b7bd593071700`
(the committed Linux proof), plus the browser working-tree changes. These are development-feed
results; no combined NuGet release or GitHub Actions execution is claimed for this browser change.

| Check | Executed result |
| --- | --- |
| Build host | VirtualBox Ubuntu 24.04.5 x64, .NET SDK 10.0.401, wasm-tools runtime 10.0.12 |
| Package-only Release publish | Core, browser transport and Currency restored from local archives into a fresh cache; no MAUI/mobile/WinUI dependencies |
| JavaScript packaging | All three published ES modules matched their NuGet archive bytes; the entire static publish output was fingerprinted |
| Google Chrome 155.0.8059.80, Windows 11 x64, `en-US` | Currency and managed/JavaScript transport checks passed; reload and orderly disposal passed |
| Playwright Firefox 157.0, Windows 11 x64, `en-US` | Same Currency and transport scenarios, reload and orderly disposal passed |
| JavaScript unit/protocol tests | 16 passed, including shared codec fixtures, Int64 extremes, cancellation races, registration rollback and callback cleanup |
| Portable / Linux regression suites | 114 portable and 18 Linux tests passed |
| Packaging regression suite | 166 passed |
| Browser input | Mouse selection of GBP, keyboard submission of `ZZZ`, and recovery with EUR passed in both engines |

Both engines returned:

```text
PASS browser: USD=$, EUR=€, GBP=£, JPY=¥; normalization, errors, concurrency, cancellation
```

The transport scenarios execute inside the published .NET WebAssembly app. They cover binary and
standard-codec messages, exact Int64 roundtrips, concurrent replies, JavaScript-to-managed callbacks,
structured errors, cancellation in both directions, timeouts, independent sessions, pending-request
disposal and failed module initialization. Each engine loads the app twice under `/currency/` and
produces exactly one `CURRENCY_AVALONIA_CLOSED` marker per load, despite repeated close calls.

Evidence is retained under `artifacts/shared-hosting-browser`: `inputs.json`, `source-files.json`,
per-engine `verification.json`, console logs and screenshots. The build and browser hosts are
recorded separately. The browser runner verifies every published file against the build manifest
before serving it; each result includes that manifest's SHA-256. Source snapshots are explicitly
marked dirty and documentation added afterward does not become part of an earlier run.

Chrome logged Avalonia WebGL renderer-query warnings; Firefox logged a deprecation warning for the
.NET-generated WebAssembly `try` instruction. Neither engine reported a runtime, network or console
error during the passing checks. The [Browser guide](currency-avalonia-browser.md) documents the
static-host MIME requirements, main-thread scope, lifecycle behavior and reproduction commands.

The new browser workflow is defined but has not been run on GitHub. DOM views, event-stream adapter
helpers, other plugins, Safari/WebKit, mobile browsers, worker hosting and BFCache behavior require
separate acceptance. Explicitly awaited cleanup is tested; a browser cannot guarantee asynchronous
cleanup completion while destroying a tab. This proof does not complete milestone 0.2.

## Avalonia on Linux

**C++ adapter update, 2026-10-17:** the working tree based on `9c1a155` replaces the
Linux Currency and AudioStreamer C# implementations with C++ modules behind a reusable
C ABI bridge. Ubuntu 24.04.5 x64 verification passed 18 Currency/transport tests, six
AudioStreamer integration tests, native C++ audio/codec tests with address and undefined-behavior
sanitizers, and 171 packaging checks. Both fresh package-only Avalonia applications passed;
Currency also passed normal window-manager close and AudioStreamer passed paused-playback
shutdown, host recreation and minimize/restart. Runtime verifiers matched the native `.so`
files to their local packages. No workflows ran. See the [native module contract](../native/linux/README.md)
and [AudioStreamer record](audio-verification.md) for scope and local evidence paths.

The earlier Linux proof below predates the C++ conversion.

Local execution on **2026-10-17** used the working tree based on
`ffe8abe34b0f4d82b46de82457e9bd50ff59e2aa`, plus the Linux implementation changes.
This is uncommitted development evidence, not a clean-commit CI or release acceptance record.

| Configuration | Executed result |
| --- | --- |
| VirtualBox 7.0.20, Ubuntu 24.04.5 x64, kernel 6.8.0-142, .NET SDK 10.0.401, ICU 74.2 | Linux VM booted; all checks below executed inside the guest |
| Portable Release suite | 114 passed, including existing Windows transport regressions |
| Linux native Release suite | 18 passed: real ICU lookups and errors, reentrant calls, messages, streams, cancellation, concurrency and disposal |
| Packaging/CI tooling | 161 passed |
| Fresh-cache package consumer, `linux-x64`, Avalonia 12.1.4 | All three archives matched the local feed; no MAUI/mobile/WinUI dependencies; build had zero warnings/errors |
| X11 through Xvfb, verification mode | Complete native Currency scenarios passed; exactly one disposal marker; exit code zero |
| X11 through Xvfb and Openbox, interactive mode | Window remained open after verification; window-manager close completed asynchronous disposal and exited zero; screenshot captured |

The native runtime result was:

```text
PASS linux: USD=US$, EUR=€, GBP=£, JPY=JP¥; normalization, errors, concurrency, cancellation
```

The dedicated VM is `NativeChannels-Ubuntu-24.04`, with the checkout at
`/home/ubuntu/native-channels`. SSH is restricted to host loopback port 22223 and uses the host's
existing SSH key (`ssh -p 22223 ubuntu@127.0.0.1`). The older `Ubuntu` VM was inspected and shut down;
its installed system was not modified. The new VM uses a checksum-verified official Ubuntu cloud image.

Evidence is retained in `artifacts/shared-hosting-linux/linux-x64/verification` and `window-close`:
runtime logs, package/source/executable hashes, environment metadata, loaded ICU paths and screenshot.
The source-file manifest records the exact tested working tree; documentation added after a run
does not retroactively become part of that run. The [Linux guide](currency-avalonia-linux.md) gives
reproduction commands, and the dedicated workflow is implemented but has not been executed on GitHub.

An initial Alt+F4 automation attempt intermittently failed to close the window. Its failed log is
retained under `artifacts/shared-hosting-linux/failed-alt-f4`. The runner now waits for an EWMH
window manager and requests normal closure through `wmctrl`, avoiding keyboard-focus dependence.

Xvfb is a real X11 server with a virtual display. These results do not establish physical desktop
input/graphics behavior, Wayland, Linux ARM64, native view hosting, other reference plugins,
trimming/AOT, or completion of milestone 0.2. No packages were published.

## Current local Avalonia checks — 2026-10-17

These runs used the local working tree on `milestone-0.2-shared-hosting`, based on
`28292efc0bdd495cfa92c5ce8d6abb434a4ece88`, and platform-specific development packages.
These runs preceded the template migration below. All four apps used the same shared UI
and native checks, now located in `samples/Avalonia/Currency/Currency/Views/MainView.axaml.cs`.

| Platform | Executed environment | Release runtime | Result |
| --- | --- | --- | --- |
| [Android](#avalonia-android-device-2026-10-17) | Physical CPH2581, Android 16 | `android-arm64` | Native checks passed; user confirmed the app works |
| [iOS](#avalonia-ios-device-2026-10-17) | Physical iPhone 12 Pro, iOS 27.0.1 | `ios-arm64` | Native checks passed; user confirmed the app works |
| [Native macOS](#avalonia-on-native-macos) | macOS 26.2 ARM64, AppKit | `osx-arm64` | Native checks, UI checks, and orderly disposal passed; interactive relaunch passed |
| [Mac Catalyst](#avalonia-on-mac-catalyst) | macOS 26.2 ARM64, UIKit/Catalyst | `maccatalyst-arm64` | Native and UI checks passed; user confirmed Cmd+A and Backspace manually |

The shared native checks cover USD/EUR/GBP/JPY, normalization, structured errors, invalid input,
concurrency, cancellation, and recovery. The iOS and Catalyst compilations reported the existing
`Avalonia.DesignerSupport` `IL2104` trimming warning; Android and native macOS built without warnings.
Native macOS x64 also compiled successfully, but Intel execution was not tested.

Earlier regression checks in this working-tree session passed **114 portable .NET tests** and
**146 packaging/CI-tool tests**, Swift checks, iOS/Catalyst transport compilation, source formatting,
workflow lint, and a documentation build/link check. Their saved logs are `artifacts/macos-*`.
Those broader checks preceded the later device runs and packaging-helper/documentation edits;
they have not been rerun against every final change. The device runs are local Currency-sample
evidence, not a new CI run, combined-feed release acceptance, or completion of milestone 0.2.

## Avalonia template migration — 2026-10-17

The official `Avalonia.Templates` 12.1.4 `avalonia.xplat` generator created the solution,
now located at `samples/Avalonia/Currency/Currency.slnx`. The existing application was moved into its shared `Currency`
project and generated platform heads. Native libraries still resolve from local NuGet archives;
only the shared application project reference is permitted by the package verifier.

Fresh Release restores and builds passed for Android ARM64, iOS ARM64, native macOS ARM64,
and Mac Catalyst ARM64. Native macOS verified AppKit Mach-O targets, all Currency scenarios, asynchronous disposal,
and exit code zero. The migrated Catalyst app passed the native scenarios, EUR shortcut, and
Enter-submitted invalid-code error. The migrated Android APK was installed on the connected
CPH2581 and returned the complete native PASS result. The signed iOS app installed on the
iPhone 12 Pro, but its launch was denied because the device was locked; post-migration iPhone
runtime verification remains pending. The native macOS app also relaunched interactively.
The iOS and Catalyst builds retain the existing `Avalonia.DesignerSupport` `IL2104` warning;
Android and native macOS builds reported zero warnings.

Packaging/CI-tool tests passed **149 tests**. The generated and migrated C# files passed formatting,
the changed workflows passed actionlint, and all 55 documentation pages passed local-link checks.
These migration checks do not repeat the earlier portable-library or Swift suites because their
sources did not change during the move. Browser remains the generated scaffold, with no native
Currency adapter; Windows and Linux desktop execution are not claimed.

Logs, generator provenance, and native macOS evidence are saved under
`artifacts/avalonia-template-migration/`. The native macOS x64 compilation and manual Catalyst
keyboard observations above predate the migration; they were not repeated for this layout change.

After those native runs, the whole Currency solution was nested under
`samples/Avalonia/Currency/` so other Avalonia samples can sit alongside it. For this folder-only
move, package restore/hash verification, the shared UI build, all 149 packaging tests,
formatting, workflow lint, and documentation links passed again. MSBuild evaluation verified
all five platform configurations resolve the shared project, package properties, and source files
at the new location. Native runtime checks were not repeated for this second move. Its logs are
under `artifacts/avalonia-sample-layout/`.

Before pushing, the two incoming Windows commits were integrated into the generated Desktop
project. It now selects native AppKit on macOS and Win32 on Windows, while preserving Windows
result-file reporting and verification through normal window closing. The Windows x64 Release
head cross-compiled on macOS with zero warnings/errors against locally built Core, transport,
and Currency packages; restored hashes matched all three local archives. The merged suite passed
**156 packaging/CI tests**, formatting, workflow lint, and **56 documentation pages** with link checks.
Both desktop target configurations were evaluated. Windows runtime execution was not repeated
here; the separate Windows evidence below remains scoped to its original source. Logs are under
`artifacts/push-integration/`.
## NuGet preview 2 — 2026-10-17 {#nuget-preview-2}

Version **0.1.0-preview.2** is published from source
[`b342e501fc64ec80e43a6974984ec26fa3e4a291`](https://github.com/Almis90/native-channels/tree/b342e501fc64ec80e43a6974984ec26fa3e4a291).
These exact-source gates passed on their first attempt:

| Gate | Result |
| --- | --- |
| [All / Runtime](https://github.com/Almis90/native-channels/actions/runs/39025734636) | Core and native checks passed; platform-feed consumers executed on Android x64, iOS simulator ARM64 and Mac Catalyst ARM64; Windows x64/ARM64 compiled |
| [Verify distribution](https://github.com/Almis90/native-channels/actions/runs/39026686762) | All seven combined-feed consumers passed: Android x64/ARM64, iOS simulator/device ARM64, Catalyst, and Windows x64/ARM64; execution covered Android x64 and iOS simulator ARM64 |
| Managed API compatibility | All five final packages passed comparison against the hash-pinned published `0.1.0-preview.1` baseline, including Windows assemblies |
| [Publishing dry run](https://github.com/Almis90/native-channels/actions/runs/39027642340) | Five framework packages and five symbol archives passed candidate validation |
| [NuGet publication](https://github.com/Almis90/native-channels/actions/runs/39027833088) | The identical candidate passed validation and was uploaded through Trusted Publishing and the protected environment |

All seven distribution results use feed SHA-256
`ea9a33d18951bca26cfbbc9418f34d206eabb516ad1d20d4f58fc946dd531806`.
iOS device compilation was unsigned. Compilation does not establish physical-device or Windows
desktop execution, and Catalyst execution with a platform feed is separate from combined-feed runtime acceptance.

All five packages and their symbols are downloadable from NuGet.org:
[Core](https://www.nuget.org/packages/NativeChannels.Core/0.1.0-preview.2),
[Android binding](https://www.nuget.org/packages/NativeChannels.Native.Android/0.1.0-preview.2),
[Apple binding](https://www.nuget.org/packages/NativeChannels.Native.Apple/0.1.0-preview.2),
[platform transports](https://www.nuget.org/packages/NativeChannels/0.1.0-preview.2), and
[MAUI integration](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.2).
All ten downloaded archives matched the reviewed candidate's file contents, excluding only the
NuGet-added repository signature. The project URL, icon, README, descriptions and release notes were
checked in the package archives; NuGet lists the version as a prerelease. Reference plugins were not published.

Post-publication checks use separate projects outside the repository, a fresh private package cache,
NuGet.org as the only source, and no project references or fallback folders:

| Public-feed check | Result |
| --- | --- |
| Portable Core consumer | Restore and execution passed: method-channel result `42`, with matching diagnostic start and successful completion notifications |
| New .NET MAUI application | Restore and Android ARM64 Release build passed with trimming and AOT, zero warnings and errors; `UseNativeChannels()` and diagnostics configuration compiled |
| Package provenance | All dependencies came from NuGet.org; all resolved NativeChannels packages used `0.1.0-preview.2` |

The [release evidence](https://almis90.github.io/native-channels/verification/nuget-preview2-release.json)
records the exact runs, package hashes, public-download comparisons and consumer results.
The public-feed MAUI application compiled without a custom native module and did not run on a device.
Native round-trip execution comes from the exact-candidate CI above, whose package contents matched
NuGet.org. Later documentation commits do not change the recorded release source or archives.

The native performance harness is included, with local Catalyst observations recorded below.
Android/iOS performance profiling is deferred; Windows profiling and the hosted profiling workflow
remain unexecuted. Functional runtime CI and compilation do not establish long-running memory stability.

## Earlier local checks — 2026-10-15

The initial preview includes all four channel contracts, the MAUI host, four platform transports,
and four reference plugins. Local checks on 2026-10-15:

| Check | Result |
| --- | --- |
| Portable Release tests | 98 passed, including the VolumeMonitor restart regression |
| Packaging-tool tests | 32 passed |
| Kotlin JVM tests | 14 bridge/protocol tests and two Currency tests passed |
| Swift tests | Bridge, codec, cancellation, stream, and Currency checks passed |
| Android native adapters | Bridge, sample, and all four plugin AARs compiled |
| Apple native adapters | Bridge, sample, and all four plugin XCFrameworks compiled for device, simulator, and Catalyst slices |
| Local mobile packages | Five framework and four plugin archives built and passed native-asset and metadata inspection; these local archives did not include Windows; combined packaging passed in later CI |
| Source formatting and workflows | C#, Kotlin, Swift, and actionlint checks passed |
| Documentation | 50 pages built; local links, fragments, and assets checked |

These local source-tree checks are separate from the exact-commit CI and publication evidence below.
They did not publish packages or create GitHub releases.

## Preview 2 preparation — local checks

The `0.1.0-preview.2` changes first received local validation, separate from the exact-source
publication evidence recorded on this page. The five mobile framework packages passed
metadata/native-asset inspection and API comparison
against the hash-pinned NuGet.org `0.1.0-preview.1` archives. A deliberately removed method signature
was rejected with `CP0002`. The later release distribution gate passed API comparison for all five
combined packages, including their Windows targets.

The native profile executed on ARM64 Mac Catalyst in Release using local package references,
.NET 10.0.12 and Mac Catalyst 26.2.0. It measured a working tree based on
`5d5a83f7f9f9eec8974108de39d2758a8f053480` with the profiling changes still uncommitted.
The [recorded summary](https://almis90.github.io/native-channels/verification/native-profile-catalyst-preview2.json)
includes that dirty-tree flag, package hashes, adapter hashes and measurements. It is a development
observation, not final-source or combined-feed release acceptance.

| Native profile check | Result |
| --- | --- |
| Catalyst execution | 300-second workload plus five seconds idle; 2,532 cycles, 284 memory samples, zero active subscriptions at completion |
| Round trips | 256 measured calls after 32 warmups each: Int32 echo, reentrant method, and reentrant 4 KiB binary message |
| Cleanup | Handle disposal, live subscription teardown, cooperative cancellation, and 254 native view recreation cycles completed |
| Native compilation | Kotlin AAR and Swift iOS device/simulator/Catalyst framework slices compiled with the memory endpoints |
| Windows profile integration | Compiled for x64 and ARM64 in the preview 2 framework CI; profiling execution remains unverified |
| Not yet executed | Profiling on Android, iOS and Windows; the new hosted profiling workflow; repeated multi-hour native soaks |

Physical footprint rose from 66,307,080 to 87,098,376 bytes, while resident size fell and managed
retained bytes grew. These are different counters, not an isolated native leak measurement.
No GC was forced. Longer comparable runs and heap inspection are needed to explain retention;
this run does not establish leak-free behavior or device-wide performance guarantees.
See [performance](performance.md#measure-real-native-round-trips-and-memory) for reproducible commands.

A [15-minute Catalyst follow-up](https://almis90.github.io/native-channels/verification/native-profile-catalyst-preview2-followup.json)
at clean source `9db8abff17b18f70a65da64f45ccdfc04f102c26` completed 7,371 cycles with zero active
subscriptions at completion. Physical footprint grew from 76.19 to 118.75 MiB; native malloc stayed
around 46–48 MiB and native bridge/view-host counts stayed constant across four heap snapshots.
A separate five-minute diagnostic run returned the managed heap near its startup size after forced
collection. This supports delayed collection as one contributor to growth; the unforced run did not
reach a memory plateau, and small unattributed malloc blocks remain unexplained. The collection
instrumentation was removed. These development observations do not establish indefinite memory
stability or performance on Android, iOS or Windows.

## First-preview framework CI verification {#framework-ci-verification}

On 2026-10-17, [All / Runtime verification](https://github.com/Almis90/native-channels/actions/runs/38980871000)
and [Verify distribution](https://github.com/Almis90/native-channels/actions/runs/38982305944)
passed at source `09ae4eb862bc142c0281ded67413ea859e179c6d`, the source of published `0.1.0-preview.1`.

The framework run built shared Core once, verified platform feeds, and executed the framework sample
on Android x64, the iOS simulator, and Mac Catalyst. The distribution run assembled the five final
framework packages and symbol archives, then verified seven package-only consumer configurations:

| Combined-feed consumer | Compiled | Executed |
| --- | --- | --- |
| Android x64 | Passed | Passed in emulator |
| Android ARM64 | Passed | Not run by this workflow |
| iOS simulator ARM64 | Passed | Passed in simulator |
| iOS device ARM64 | Passed, unsigned | Not run by this workflow |
| Mac Catalyst | Passed | Not run with the combined feed |
| Windows x64 | Passed | Not run by this workflow |
| Windows ARM64 | Passed | Not run by this workflow |

All seven results reference the same feed SHA-256:
`10a4e436a17b35d32389d2d9626dfe47b39e60d9dc79aedd7a2425a30255ac4e`.
The distribution artifact contains the per-configuration results and package audit. Runtime checks
cover framework methods, messages, events, and native views, including 32 reconnect cycles;
example plugins are separate. These verification runs did not publish packages; the separate
publication is recorded below.

This evidence applies only to the recorded source and archives. Later changes, including packaged
README content, require fresh same-commit validation before publication. Catalyst execution with a
platform feed does not establish combined-feed runtime acceptance, and compilation does not establish
physical-device or Windows desktop execution.

## Shared hosting on Android

On 2026-10-17, [Shared hosting / Android Currency](https://github.com/Almis90/native-channels/actions/runs/38970474333/job/117209907244)
passed for PR head `e7483451976582371c2fc1b6cf75b41bd5ef2a02`. GitHub tested merge revision
`984e4aacd020a894c73522e4a53a9e3c18421738`; this is the source revision recorded in the package evidence.

Both package-only Release apps compiled with zero warnings/errors and executed on the same Android
API 35 x64 emulator. MAUI and Avalonia returned `USD=$, EUR=€, GBP=£, JPY=¥`, and both passed
normalization, structured native errors, invalid input, concurrent calls, cancellation and recovery.
Both apps compile the same verification source.

The two fresh NuGet caches contained identical `NativeChannels.Core`, `NativeChannels`, Android
binding and Currency archives. Avalonia's dependency graph contained no `NativeChannels.Maui` or
`Microsoft.Maui.*` packages. The Android development package `Plugin.Maui.Currency` 1.1.0-alpha.1 had
SHA-256 `e10129f0347ccfceab5d2d709e360c94febe1661ee252e90789cab1bd7a12270` in both hosts.
The `shared-hosting-android` artifact retains all four package hashes, app logs and `verification.json`.

All framework compile checks, workflow lint, documentation and Windows C++/Rust checks passed in
that run. This proves the Currency method-call slice of milestone 0.2. It does not establish Avalonia
native views, incoming UI callbacks, event lifecycle, physical-device or other-platform support;
combined release-feed acceptance and fresh iOS simulator execution remain separate checks.

### Physical Android phone — 2026-10-17 {#avalonia-android-device-2026-10-17}

The local working tree on `milestone-0.2-shared-hosting`, based on
`28292efc0bdd495cfa92c5ce8d6abb434a4ece88`, built the updated Avalonia Currency app in Release
for `android-arm64` with zero warnings/errors. A fresh consumer cache matched all four local
Android package archives. The APK was installed on a physical CPH2581 running Android 16 and
opened through its explicit launcher activity. The user confirmed that the app works.

The running process logged
`CURRENCY_AVALONIA PASS android: USD=US$, EUR=€, GBP=£, JPY=JP¥; normalization, errors, concurrency, cancellation`.
The shared verification also checks invalid input and recovery. Build, restore, launch, and
process-log records are under `artifacts/device-testing/avalonia-android-*`; the extracted result
is `artifacts/device-testing/currency-avalonia-android-result.txt`.

This records physical-device execution of the Avalonia Currency method-call sample. The MAUI
sample, other channel contracts, and general activity/background lifecycle were not retested on
this phone in this run.

## Shared hosting on iOS

On 2026-10-17, [Shared hosting / iOS Currency](https://github.com/Almis90/native-channels/actions/runs/38985319526/job/117257484126)
passed for PR head `4f648cb48c01556aef9fce0cc07dcfc45856c7ac`. GitHub tested merge revision
`c1198009808f617c30da704ca84d96a328eeb222`, which is recorded in the package evidence.

The Swift Currency adapter built and passed its native checks. The package-only MAUI and Avalonia
apps compiled in Release for `iossimulator-arm64` and executed on the same iPhone 17 Pro simulator.
Both returned `USD=$, EUR=€, GBP=£, JPY=¥` and passed normalization, structured native errors,
invalid managed input, concurrent calls, cancellation and recovery. Saved screenshots show both
applications displaying the USD result and successful native checks.

Separate fresh caches contained identical `NativeChannels.Core`, `NativeChannels`, Apple binding
and Currency archives. Avalonia's graph contained no `NativeChannels.Maui` or `Microsoft.Maui.*`
dependencies. The iOS development package `Plugin.Maui.Currency` 1.1.0-alpha.1 had SHA-256
`74cc039391e5e23243b20e06e8b0cb2417f3dee3f0c6d846b534d70ba9e9f2b1` in both hosts and contained
no Android AAR. The `shared-hosting-ios` artifact retains all four package hashes, source revision,
runtime results, logs and screenshots; its `verification.json` records `success: true`.

The MAUI app built with zero warnings/errors. Avalonia built with zero errors and one `IL2104`
trimming warning from `Avalonia.DesignerSupport` 12.1.4. The warning remains visible; the successful
simulator run covers this sample's tested paths, not every trimming-sensitive Avalonia feature.

The [Android regression](https://github.com/Almis90/native-channels/actions/runs/38985319526/job/117257191220)
also passed at the same source: both Release apps built with zero warnings/errors and returned
matching results on an API 35 x64 emulator. Archive identity is checked between the two hosts
within each platform feed; Android and iOS archives contain different native assets. All framework
compile jobs, workflow lint, documentation and Windows C++/Rust checks passed. Local regression
checks passed 114 .NET tests and 139 packaging/CI tests.

These are development-package Currency method-call checks. Full framework runtime jobs were
skipped at this run's Compile depth; both Currency host pairs executed regardless. This evidence
does not establish physical iPhone execution, signing or App Store distribution, general scene or
background lifecycle behavior, other channel contracts/native views, or final combined-feed release
acceptance. It does not complete milestone 0.2 or publish a new framework version.

### Physical iPhone — 2026-10-17 {#avalonia-ios-device-2026-10-17}

The local working tree on `milestone-0.2-shared-hosting`, based on
`28292efc0bdd495cfa92c5ce8d6abb434a4ece88`, built the updated Avalonia Currency app in Release
for `ios-arm64` from a fresh iOS development-package feed. The signed app was installed on an
iPhone 12 Pro running iOS 27.0.1 and opened after the development profile was trusted. The user
confirmed that the app works on the phone.

The result retrieved from the app's Documents container was
`PASS apple: USD=US$, EUR=€, GBP=£, JPY=JP¥; normalization, errors, concurrency, cancellation`.
The shared verification also exercises invalid input and recovery. Build, installation and
result-copy records are saved under `artifacts/device-testing/avalonia-ios-*`; the returned
result is `artifacts/device-testing/currency-avalonia-ios-result.txt`.

This records physical-device execution of the Avalonia Currency method-call sample with
development signing. It does not add a physical MAUI comparison, App Store distribution,
general lifecycle or native-view coverage, or final combined-feed release acceptance.

## Avalonia on Mac Catalyst

On 2026-10-17, the local working tree on `milestone-0.2-shared-hosting`, based on
`28292efc0bdd495cfa92c5ce8d6abb434a4ece88`, built and ran the Avalonia iOS Currency sample
with `TargetFramework=net10.0-maccatalyst` in Release for `maccatalyst-arm64` on macOS 26.2.
It reused the iOS application/delegate and shared Currency view without MAUI dependencies or
library project references. The fresh consumer cache matched all four local package archives.
The app, bridge, and Currency framework each had ARM64 `MACCATALYST` Mach-O metadata.

The app returned `USD=US$, EUR=€, GBP=£, JPY=JP¥` and passed normalization, native structured
errors, invalid input, concurrent calls, cancellation, and recovery. Interactive checks passed
the EUR shortcut, `ZZZ` error through the lookup button, and Enter submission of ` eur ` with
normalization and recovery. The user confirmed that Cmd+A and Backspace work manually after
automated key input behaved differently. The app remains open for inspection.

The build succeeded with the existing `IL2104` trimming warning from
`Avalonia.DesignerSupport` 12.1.4. Build, package, restore, runtime, and Mach-O records are under
`artifacts/device-testing/avalonia-catalyst-*`. This is local Currency method-call evidence;
Intel execution, general lifecycle/native-view acceptance, distribution signing/notarization,
and same-commit CI remain unverified.

## Avalonia on native macOS

On 2026-10-17, the local working tree on `milestone-0.2-shared-hosting`, based on
`28292efc0bdd495cfa92c5ce8d6abb434a4ece88`, built and executed the package-only
[Avalonia macOS Currency sample](currency-avalonia-macos.md) in Release on macOS 26.2 ARM64.
The target is `net10.0-macos` with AppKit. The verifier checked native macOS Mach-O platform
metadata for the executable, bridge, and Currency framework and rejected UIKit/Catalyst links.

The app returned `USD=US$, EUR=€, GBP=£, JPY=JP¥` for the local region and passed normalization,
structured native errors, invalid managed input, concurrent calls, cancellation, recovery and
asynchronous session disposal before exit. Its fresh package cache matched all four local archive
hashes and contained no MAUI packages or library project references.

The desktop UI was also checked against the MAUI Currency page. The shared Avalonia view now
includes the header, symbol color, centered result, full-width action, four currency shortcuts,
and the same help text. Live macOS UI checks passed for each shortcut, Enter submission with
normalization, the `ZZZ` error via the lookup button, and recovery through the EUR shortcut.
The subsequent interactive relaunch also returned the complete native `PASS` result, saved in
`artifacts/device-testing/avalonia-macos-interactive.log`, and the user confirmed the app works.

The Swift bridge/codec/lifecycle tests, portable .NET tests, packaging-tool regressions, and
existing iOS/Catalyst transport builds also passed locally. These are working-tree checks, not
same-commit CI or release acceptance. The dedicated macOS workflow has been added but has not
been run remotely. Evidence is saved under `artifacts/shared-hosting-macos/osx-arm64`.

Release consumers compiled for ARM64 and x64 with zero warnings/errors. Native Swift slices
include both architectures. Intel execution, macOS 14 minimum-version
execution, signing/notarization, Avalonia native-view hosting and other channel contracts remain
unverified. This initial Currency proof does not complete milestone 0.2.

## Shared hosting on Windows

On 2026-10-17, `eng/verify-shared-hosting-windows.ps1` passed on a physical Windows 11 x64 PC
(OS build 26200), from clean source `3118288465f6988493627067718e5db8b2a81d27`. This was local
desktop execution, separate from the hosted Windows compilation jobs.

Fresh Windows-only framework and Currency packages were built from that revision. Both package-only
applications published in Release for `win-x64` without reported warnings or errors. MAUI and
Avalonia executed and returned `USD=$, EUR=€, GBP=£, JPY=¥`; both passed normalization, structured
native errors, invalid input, concurrent calls, cancellation and recovery. Avalonia's verification
mode closed the actual window through its normal closing handler, awaited session disposal, and
exited with code zero. All 17 Windows Currency native API tests also passed.

Separate fresh caches contained identical shared archives:

| Package | Version | SHA-256 in both hosts |
| --- | --- | --- |
| NativeChannels.Core | 0.1.0-preview.1 | `7694351f5aad6e405bcc92e9383e6f24269fc1f62d14d392e1bd46c6cd43526c` |
| NativeChannels | 0.1.0-preview.1 | `912fd9dc65762fa3032574d288e79c084bb345531a5a67ec0fb870ff0ecb4209` |
| Plugin.Maui.Currency | 1.1.0-alpha.1 | `c3937dac7f5acb9f16951ac9dc7558195c8352a229eed04da94e48c2975bc343` |

Both graphs contained no library project references or mobile channel bindings. Avalonia used its
Win32 backend with no MAUI or Windows App SDK dependency. Framework package metadata matched the
source commit. The local run `bb3c43c47daa4acc8351eb4753eee2c3` under
`artifacts/shared-hosting-windows/win-x64/` retains package/compilation records, native test results
and per-host output; `verification.json` records `sourceDirty: false` and `success: true`.
The local regression suite also passed 114 .NET tests and 145 packaging/CI tests.

These archives are development builds, distinct from the published NuGet preview despite reusing
its framework version. Both applications were unpackaged and self-contained. This does not establish
Windows 10 execution, Windows ARM64 runtime support, MSIX deployment, interactive/manual acceptance,
multi-window or OS-shutdown behavior, native view embedding, or final combined-feed release
acceptance. The required Windows shared-hosting CI job compiles x64 and ARM64 and executes native
API tests on the x64 runner; it does not claim desktop application execution. This Currency proof
does not complete Avalonia milestone 0.2. See the [Windows sample](currency-avalonia-windows.md).

## NuGet publication — 2026-10-17 {#nuget-publication-2026-10-17}

Version **0.1.0-preview.1** was published from source
`09ae4eb862bc142c0281ded67413ea859e179c6d` after the framework and distribution checks above.
The [candidate dry run](https://github.com/Almis90/native-channels/actions/runs/38983442471)
and [approved publishing run](https://github.com/Almis90/native-channels/actions/runs/38984024506)
passed. Publication used NuGet Trusted Publishing with GitHub environment approval.

All five packages and their managed symbol archives are downloadable from NuGet.org:
[Core](https://www.nuget.org/packages/NativeChannels.Core/0.1.0-preview.1),
[Android binding](https://www.nuget.org/packages/NativeChannels.Native.Android/0.1.0-preview.1),
[Apple binding](https://www.nuget.org/packages/NativeChannels.Native.Apple/0.1.0-preview.1),
[platform transports](https://www.nuget.org/packages/NativeChannels/0.1.0-preview.1), and
[MAUI integration](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.1).
Every downloaded archive's file contents matched the accepted candidate, ignoring only NuGet's
added repository signature. Example plugins were not published.

Post-publication checks used separate projects outside the repository, a fresh private package cache,
and NuGet.org as the only package source, with no project references or fallback folders:

| Public-feed check | Result |
| --- | --- |
| Portable Core consumer | Restore passed; executed a method-channel round trip returning `42` |
| New .NET MAUI application | Restore passed; `UseNativeChannels()` compiled into an Android ARM64 Release app with trimming and AOT, with no warnings or errors |
| Package provenance | All restored packages came from NuGet.org; framework version `0.1.0-preview.1` resolved transitively |
| Android minimum version | Consumer requires API 24; a lower template setting fails manifest merging, now documented in getting started |

The public-feed MAUI check compiled the integration without a custom native module; it did not run
on a device. Native round-trip execution comes from the exact-candidate CI results above, whose
package contents were compared with NuGet.org. No new physical-device, Windows desktop, or
combined-feed Catalyst execution is claimed. These records describe the published source and
archives, not later documentation commits.

## Automated checks

The portable suite covers codecs and typed values, error/null distinctions, reentrancy, FIFO entry,
concurrent completion, handler ownership, cancellation and timeout races, infinite timeouts, disposal,
independent event subscriptions, bounded buffering, Windows transport behavior, and view lifetimes.
Kotlin and Swift share protocol fixtures with the C# tests.

Package tooling checks archive paths, dependencies, source metadata, native assets, combined-platform
payloads and consumer cache hashes. Separate samples restore without project references.
See [testing](testing.md), [performance](performance.md), and [CI commands](building.md#github-actions).

## Android and Apple

The framework sample passed package-only Android emulator, iOS simulator and Mac Catalyst execution
at the source revision recorded above. This does not establish physical-device acceptance or
plugin-specific SDK behavior for those package archives.

### Manual observations — 2026-10-15

**Music was heard on both the Android emulator and iOS simulator.** Interactive checks covered
looping, pause/resume, and Stop returning an empty buffer. These observations are separate from silent
automated PCM tests and from acceptance of the current package artifacts. See
[AudioStreamer verification](audio-verification.md).

VolumeMonitor's live updates were observed on a physical Android CPH2581 running Android 16 and an
iPhone 12 Pro running iOS 27.0. After the foreground audio-session fix, Stop → change volume → Start
immediately reported the new iPhone level. See [VolumeMonitor verification](volume-verification.md#physical-device-checks-on-2026-10-15)
for the exact scope and remaining lifecycle checks.

The framework app's native buttons are small lifecycle fixtures; PdfViewer is the practical
native-view example. Mac Catalyst, iOS simulator, and physical iOS execution require separate checks.

### Physical phones and Mac examples — 2026-10-16/17

All four Release examples passed automatic startup checks and user-confirmed manual checklists on
an Android CPH2581, an iPhone 12 Pro, and an arm64 Mac running the Catalyst apps:

| Example | Manual observations |
| --- | --- |
| Currency | Symbols, lowercase input, invalid-code errors and recovery |
| AudioStreamer | Audible music, looping, pause/resume, stop/restart and volume controls |
| VolumeMonitor | Live volume updates and immediate current level after Stop/change/Start |
| PdfViewer | Three-page navigation, zoom/pan, fit, reopen and layout checks |

Phone checks also covered audio stopping on backgrounding and explicit volume-listener restart
after returning. Mac Currency and AudioStreamer checks included window resizing. Mac VolumeMonitor
initially stayed at 100%; the Core Audio correction was rebuilt locally, retested, then committed as
`f9c779f`. Its default-output-device switching remains unverified. See
[VolumeMonitor verification](volume-verification.md#mac-catalyst-correction-on-2026-10-17).

The sample source was `53b10ae`, with existing local framework/plugin packages from `b14c1ff`;
Mac VolumeMonitor used the corrected local package described above. These are example behavior
checks, not acceptance of the final combined feed. Earlier Mac launches could create two processes;
PDF Viewer exposed a shared-file conflict. A single-instance relaunch passed both automatic and
manual checks. The observations do not establish multi-instance application support.

Separately, the framework's combined-feed ARM64 Android APK from distribution run
`38960118568` at `53b10ae` passed its automatic checks on the physical Android phone. No new physical
iPhone framework self-test was executed in this session.

## Windows

Windows 11 x64 physical execution passed on 2026-10-17 at `f9c779f`: 98 portable tests, 27 Windows
plugin tests, all five application runtime checks, and user-confirmed example interactions.
The [Windows report](windows-plugins-verification.md#physical-windows-execution-2026-10-17) records
the environment and scope. Packages were Windows-only and applications were unpackaged;
combined-feed execution, ARM64 execution and MSIX deployment remain separate checks.

Use [Windows integration](windows-integration.md#combined-platform-packages) for the OS/architecture
acceptance matrix, and [Windows plugin verification](windows-plugins-verification.md) for native API,
application, and installer checks. ARM64 cross-compilation does not establish ARM64 runtime support.

## Remaining verification

- Repeat the successful framework and combined-feed gates for the next release revision when inputs change; retain exact source and package hashes.
- Run Windows ARM64 applications on ARM64 hardware; broaden OS and deployment coverage as documented.
- Execute the final combined-feed framework consumer on physical Android/iOS devices and Windows; keep example observations separate from distribution evidence.
- Verify independent iOS observer counts and additional audio routes; foreground/background example behavior was manually checked as described above.
- Verify Catalyst output-device switching, Bluetooth/headphone routes, and audio interruptions.
- Repeat longer native memory/lifecycle soaks, investigate retention, and extend the Catalyst profile
  to Android, iOS and Windows; record physical-device and hosted execution separately.
- Verify every planned Avalonia and Uno host/target combination against the [roadmap completion criteria](roadmap.md#avalonia-integration), including desktop macOS, Linux, and JavaScript/DOM browser adapters. Existing Currency and Windows/Linux audio proofs do not establish the remaining contracts or platforms.

Use the [release checklist](release-checklist.md) before tagging or distribution. Keep implementation,
compilation, runtime results, and manual observations separate in future updates.

## Avalonia interoperability port — 2026-10-17

The extra MAUI `NativeChannels.Sample` now has an Avalonia counterpart at
[`samples/Avalonia/Interop`](../samples/Avalonia/Interop/README.md), with source
heads for Android, iOS, Windows, macOS, Linux and browser. It links the existing
managed contract suite and adds Avalonia native-view lifecycle checks. Linux
implements sample behavior and GTK buttons in C++; browser uses JavaScript/DOM;
macOS adds an AppKit button factory alongside the existing UIKit factory.

Compilation checks on Ubuntu 24.04 x64 with the pinned .NET SDK 10.0.401:

| Check | Result |
|---|---|
| Linux C++17/GTK sample library (`-Wall -Wextra -Werror`) and Linux x64 head | Build passed |
| Windows x64 head, cross-built on Ubuntu | Build passed |
| Browser/WebAssembly head | Build passed |
| Three framework-package feeds and matching package-only consumers | Metadata/platform audits and archive hashes passed |

The three managed builds reported zero warnings and errors. External dependency
caches were reused to fit the VM disk; this is not fresh-cache release acceptance.
Android, iOS and macOS heads were not compiled in this pass. No apps, runtime
checks or workflows were run. The in-app **Run checks** suite remains to be
executed on all six platforms, including the new Linux callback ABI extension.
