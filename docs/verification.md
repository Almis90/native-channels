# Platform verification

This record separates implementation, compilation, runtime checks, and manual observations.
Package acceptance requires verification of the exact source revision and generated archives.

| Status | Meaning |
| --- | --- |
| Planned | Roadmap scope; not implemented or verified support on `main` |
| Implemented | Code exists for the target |
| Compiled | The specified configuration built successfully |
| Executed | The scenario ran on the stated environment |
| Unverified | The scenario has not been checked on that environment |

The current support record covers MAUI. Complete Avalonia integration (0.2) and Uno integration
(0.3) are [planned](roadmap.md) for Android, iOS, Windows, desktop macOS, Linux, and browser/WebAssembly.
None of those additional host combinations is accepted on `main` yet. The Android Currency proof
is partial milestone work; it does not establish other contracts, targets, or released support.
Future records must distinguish unavailable OS/browser capabilities from checks that have not run.

## Current checkout

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
| Local mobile packages | Five framework and four plugin archives built and passed native-asset and metadata inspection; combined Windows packaging remains pending |
| Source formatting and workflows | C#, Kotlin, Swift, and actionlint checks passed |
| Documentation | 50 pages built; local links, fragments, and assets checked |

These local source-tree checks are separate from the exact-commit CI evidence below. No NuGet
packages or GitHub releases have been published.

## Framework CI verification

On 2026-10-16, [All / Runtime verification](https://github.com/Almis90/native-channels/actions/runs/38870771035)
passed at source `a8274534d905707460c39a7e9b433707090d2cee`.

| Check | Evidence |
| --- | --- |
| Shared Core | 59 Core and eight portable Windows tests; 71 packaging/CI tests; Core packed once |
| Platform packages | Android, iOS, Catalyst and Windows consumed the exact shared Core archive; fresh consumer caches matched their local feeds |
| Android | Release package-only sample compiled; saved APK executed on a separate emulator runner |
| iOS | Release simulator sample compiled; saved app executed on a separate iOS simulator runner |
| Mac Catalyst | Release sample compiled; saved app launched and passed on a separate Mac runner |
| Windows | Package-only Release consumers compiled independently for x64 and ARM64; hosted desktop execution was not run |
| Runtime scope | Framework method, message, event and native-view checks, including 32 reconnect cycles; no example-plugin execution |
| Combined packages | [Release package gate](https://github.com/Almis90/native-channels/actions/runs/38872653521) passed at the same source revision: five framework packages and symbols assembled and inspected; nothing published |

The first trial exposed an Apple workflow variable named `PLATFORM` that changed MSBuild output
paths. The corrected run above passed both Apple artifact handoffs and runtime checks.
The verification artifact records compilation and execution separately, including successful
compilation when a later job step fails.

This evidence applies to the recorded source and platform-specific feeds. Subsequent workflow
cleanup does not turn it into an exact-commit runtime result for a newer revision. Combined-feed
consumer checks, iOS device compilation/execution, optional example handoffs, and physical-device
acceptance remain separate checks before distribution.

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

- Execute combined-feed consumers for the intended release revision; compile the iOS device target and record physical-device execution separately.
- Run Windows ARM64 applications on ARM64 hardware; broaden OS and deployment coverage as documented.
- Execute the final combined-feed framework consumer on physical Android/iOS devices and Windows; keep example observations separate from distribution evidence.
- Verify independent iOS observer counts and additional audio routes; foreground/background example behavior was manually checked as described above.
- Verify Catalyst output-device switching, Bluetooth/headphone routes, and audio interruptions.
- Complete long-running native heap/lifecycle stress and measured end-to-end performance checks.
- Verify every planned Avalonia and Uno host/target combination against the [roadmap completion criteria](roadmap.md#avalonia-integration), including desktop macOS, Linux, and JavaScript/DOM browser adapters. None is supported on `main` yet.

Use the [release checklist](release-checklist.md) before tagging or distribution. Keep implementation,
compilation, runtime results, and manual observations separate in future updates.
