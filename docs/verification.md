# Platform verification

This record separates implementation, compilation, runtime checks, and manual observations.
Package acceptance requires verification of the exact source revision and generated archives.

| Status | Meaning |
| --- | --- |
| Implemented | Code exists for the target |
| Compiled | The specified configuration built successfully |
| Executed | The scenario ran on the stated environment |
| Unverified | The scenario has not been checked on that environment |

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

## Windows

Windows transport behavior passes eight portable regression tests. These test the channel transport,
not WinUI hosting or packaged application deployment. The current preview still requires combined-feed
consumer compilation and execution on the supported Windows hosts.

Use [Windows integration](windows-integration.md#combined-platform-packages) for the OS/architecture
acceptance matrix, and [Windows plugin verification](windows-plugins-verification.md) for native API,
application, and installer checks. ARM64 cross-compilation does not establish ARM64 runtime support.

## Remaining verification

- Execute combined-feed consumers for the intended release revision; compile the iOS device target and record physical-device execution separately.
- Run Windows ARM64 applications on ARM64 hardware; broaden OS and deployment coverage as documented.
- Test framework, Currency, AudioStreamer, and PdfViewer behavior on physical Android/iOS devices.
- Verify VolumeMonitor background/resume after its iOS fix, independent iOS observer counts, and audio routes.
- Verify plugin-specific Mac Catalyst runtime behavior, Bluetooth/headphone routes, and audio interruptions.
- Complete long-running native heap/lifecycle stress and measured end-to-end performance checks.
- Verify each Avalonia host/platform combination separately; the Android Currency proof does not
  establish other contracts or platforms.

Use the [release checklist](release-checklist.md) before tagging or distribution. Keep implementation,
compilation, runtime results, and manual observations separate in future updates.
