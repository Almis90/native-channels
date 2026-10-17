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

The `0.1.0-preview.2` changes have local validation, separate from the published-preview evidence
below. The five mobile framework packages passed metadata/native-asset inspection and API comparison
against the hash-pinned NuGet.org `0.1.0-preview.1` archives. A deliberately removed method signature
was rejected with `CP0002`. The combined Windows package comparison still requires the distribution gate.

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
| Windows profile integration | Implemented; compilation and execution on Windows remain unverified |
| Not yet executed | Profiling on Android and iOS; the new hosted profiling workflow; longer repeated native soaks |

Physical footprint rose from 66,307,080 to 87,098,376 bytes, while resident size fell and managed
retained bytes grew. These are different counters, not an isolated native leak measurement.
No GC was forced. Longer comparable runs and heap inspection are needed to explain retention;
this run does not establish leak-free behavior or device-wide performance guarantees.
See [performance](performance.md#measure-real-native-round-trips-and-memory) for reproducible commands.

## Framework CI verification

On 2026-10-17, [All / Runtime verification](https://github.com/Almis90/native-channels/actions/runs/38980871000)
and [Verify distribution](https://github.com/Almis90/native-channels/actions/runs/38982305944)
passed at source `09ae4eb862bc142c0281ded67413ea859e179c6d`, the source of the published preview.

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
- Verify every planned Avalonia and Uno host/target combination against the [roadmap completion criteria](roadmap.md#avalonia-integration), including desktop macOS, Linux, and JavaScript/DOM browser adapters. None is supported on `main` yet.

Use the [release checklist](release-checklist.md) before tagging or distribution. Keep implementation,
compilation, runtime results, and manual observations separate in future updates.
