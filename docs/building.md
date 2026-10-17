# Building and verifying

For Windows, use [Windows integration](windows-integration.md).
`./eng/verify-windows.ps1` builds Windows packages and runs a fresh-cache consumer; add `-BuildOnly`
for environments without an interactive desktop. The commands below cover Android and Apple.
Source packages use unreleased framework `0.1.0-preview.2` and Currency `1.1.0-alpha.1`. The package-feed workflow
assembles [combined Windows/mobile packages](windows-integration.md#combined-platform-packages)
before consumer verification; local builds on one host produce only that host's platform inputs.

Pinned tools: .NET SDK/workload set 10.0.401, C# 14, MAUI 10.0.110, Xcode 26.6, JDK 21,
Android SDK platform/build tools 36. The bootstrap installs .NET locally in `.tools/dotnet`.
Set `JAVA_HOME` and `ANDROID_HOME` for nondefault paths.

```sh
bash eng/bootstrap.sh --platforms
bash eng/dotnet.sh test
bash eng/build-android.sh
python3 eng/build-apple.py
bash eng/pack.sh
```

`NativeChannels.slnx` is portable, so plain `dotnet test` works with the pinned SDK and no mobile workloads.
`eng/NativeChannels.Platforms.slnx` builds the complete platform libraries. Native framework builds include
iOS device arm64, simulator arm64/x64, and Mac Catalyst arm64/x64 slices.

The public sample has **package references only**, with explicitly supplied native adapter artifacts.
The restore helper first removes the consumer’s generated `bin`/`obj` directories and previous
isolated package cache to prevent stale AOT or linker state and unbounded cache growth. Restore into a new isolated package cache each time the bridge, dependencies, bindings, or packaging
changes. The restore helper copies third-party package archives into a local feed for speed, excluding
all NativeChannels packages. Library packages always come from `artifacts/packages`.

```sh
bash eng/restore-sample.sh -p:TargetFrameworks=net10.0-ios -r iossimulator-arm64
bash eng/dotnet.sh build samples/Maui/ChannelExplorer -f net10.0-ios -r iossimulator-arm64 --no-restore
bash eng/verify-ios-sample.sh SIMULATOR_UDID

bash eng/restore-sample.sh -p:TargetFrameworks=net10.0-android
bash eng/dotnet.sh build samples/Maui/ChannelExplorer -f net10.0-android --no-restore
bash eng/verify-android-sample.sh EMULATOR_SERIAL
```

Repeat with `-c Release` for build and `Release` as the verification script's second argument.
For a smaller Android build targeting a single ABI, restore/build with `-r android-arm64` and pass
`android-arm64` as the verification script's third argument. CI uses `android-x64` for its emulator.
Restore also checks that no project references entered the graph and that cached NativeChannels archives
are byte-for-byte identical to the local packages. `python3 eng/inspect-packages.py` inspects native assets.
For compile checks use `-r ios-arm64 -p:EnableCodeSigning=false` or `-r maccatalyst-arm64`, restoring
with that same target/RID first. Device execution requires provisioning and actual hardware.

Native tests:

```sh
mkdir -p artifacts
native/android/gradlew -p native/android --no-daemon :library:testReleaseUnitTest
xcrun swiftc -swift-version 5 native/apple/Sources/*.swift native/apple/Tests/main.swift -o artifacts/apple-bridge-tests
artifacts/apple-bridge-tests
```

The original generic bridge consumer remains under `tests/PackageConsumer` as an interoperability
regression test. It contains test-only probes; production channel packages expose generic operations.
The sample writes `sample-result.txt` and logs `NATIVECHANNELS_SAMPLE`. A compile pass is not a runtime pass.
See [verification](verification.md). Build and verification scripts do not publish packages or create releases.
The separate [publishing workflow](publishing.md) validates by default; uploading requires an explicit
manual selection and environment approval.

On a compatible Mac, test Catalyst independently:
```sh
bash eng/restore-sample.sh -p:TargetFrameworks=net10.0-maccatalyst -r maccatalyst-arm64
bash eng/dotnet.sh build samples/Maui/ChannelExplorer -f net10.0-maccatalyst -r maccatalyst-arm64 --no-restore
bash eng/verify-catalyst-sample.sh
```
The script launches the sample, checks its output, and closes its process after the result.

## Local disk usage

Most checkout storage comes from the locally installed .NET workloads, isolated package caches,
and compiled applications. Restore helpers retain one current cache per consumer; third-party
package archives remain in `.tools/dependency-feed` for later offline restores. Failed restores
record their cache too, so the next restore can remove it.

Stop local builds and restores before applying cleanup. Preview unused consumer caches with:

```sh
python3 eng/clean-local.py
```

To remove those caches and generated `bin`/`obj` directories for tracked projects:

```sh
python3 eng/clean-local.py --builds --apply
```

The cleanup preserves caches named by current consumer markers or project assets, the pinned
toolchain, emulator data, local packages, native frameworks, and verification logs. Applications
whose outputs are removed need a restore/build before they can be deployed again. Source files
and Git history are unaffected. Fresh-cache package verification still uses a new empty directory
on each restore; it does not reuse the previous consumer cache.

## GitHub Actions

Start with [Verify framework](../.github/workflows/verify-framework.yml). It selects the affected
platforms, builds Core once, and passes that exact package to independent platform jobs. You do not
need to start each platform workflow or copy run IDs between build stages.

```sh
# Android change: compile and execute the framework sample.
gh workflow run verify-framework.yml --ref main -f platforms=Android -f depth=Runtime

# Shared Core change: build Core once, then verify every platform.
gh workflow run verify-framework.yml --ref main -f platforms=All -f depth=Runtime

# Select affected platforms relative to a known commit.
gh workflow run verify-framework.yml --ref main -f platforms=Auto -f base=BASE_SHA -f depth=Runtime
```

Choose `iOS`, `Catalyst`, or `Windows` to target those platforms. `Compile` is the default depth;
`Runtime` adds execution on Android, the iOS simulator, and the Mac runner. Windows remains compile-only
on hosted CI. The framework path has passed [recorded CI verification](verification.md#framework-ci-verification).
Deadlines remain limits, not performance promises.

### Automatic checks

Pull requests and pushes to `main` use **Auto / Compile**: portable tests, package inspections, native
unit tests on selected platforms, and Release compilation of the package-only framework sample.
They do not start emulators, run the sample, build example plugins, or run benchmarks/long stress loops.
Documentation-only changes finish the framework selector and summary without a Core/native build.

| Changed files | Selected platform builds |
| --- | --- |
| Core, shared transport/MAUI code, shared build configuration/workflows, unknown paths | All |
| Android native code or Android platform implementation | Android |
| Shared Apple code or Apple bindings | iOS and Catalyst |
| iOS-only / Catalyst-only application code | iOS / Catalyst respectively |
| Windows native or platform code | Windows |
| C++/Rust interop sources, test project, verifier, or dedicated workflow | Windows |
| Portable framework tests, package-tool tests, or the platform selector only | Core tests only |
| Documentation, examples, benchmarks only | None; use their separate workflows |

Project and dependency definitions conservatively select all platforms, except the explicitly
Windows-owned `native/windows/interop/` and `tests/NativeChannels.Windows.Interop/` trees.
`eng/verify-windows-interop.ps1` and `.github/workflows/windows-interop.yml` also select Windows.
The Windows toolchain action/helper and the dedicated `framework-windows.yml` and `windows.yml`
workflows select Windows as well. Shared workflows, including `build-examples.yml`, still select all.
Selector changes run the shared job's routing regression tests; shared workflow changes still select
all platforms. Mixed changes combine their selections, so adding a shared change still selects all.
Auto compares the entire PR against its base, or a push against its previous commit. A documentation
commit on an existing code PR therefore still verifies that PR's code changes.
Manual Auto takes `base`; a missing/unavailable comparison selects
all platforms. Deleted and renamed paths are included. A manual platform override is useful for focused
work; it does not establish verification of unselected platforms.

[Checks / Workflows](../.github/workflows/workflows.yml) lints CI changes separately.
[Docs / Site](../.github/workflows/pages.yml) checks documentation and deploys eligible `main` changes.
Use **Framework verification result** as the required framework branch check: it always runs and
rejects failed or incomplete selected checks. Path-filtered documentation/lint workflows should not
be unconditional required checks, since unrelated changes can leave them pending.

### Shared build and independent platform jobs

The shared Core job runs the portable framework solution, including portable Windows transport tests,
then packs Core without rebuilding it. Platform jobs restore that archive through an exclusive local
source mapping. Guards compare package hashes, reject Core project-reference restoration, and fail if
Core build outputs appear downstream. Project references remain the default for local development.

| Platform | Compile stage | Optional runtime stage |
| --- | --- | --- |
| Android | Kotlin bridge/sample AARs and bridge tests; four framework packages built once; independent fresh-cache x64 and ARM64 Release sample builds | Install the saved x64 APK and check its result in one emulator |
| iOS | Shared Apple native artifacts; four iOS framework packages; fresh-cache Release simulator build | Install the saved app and check its result in one iPhone simulator |
| Catalyst | Reuse the same Apple native artifacts; four Catalyst framework packages; fresh-cache Release build | Launch the saved app directly on the Mac runner, check its result, and close it |
| Windows | Three framework packages once; independent fresh-cache x64 and ARM64 Release consumer builds | No hosted desktop execution |

Apple native preparation runs once when either Apple target is selected and produces device, simulator,
and Catalyst XCFramework slices. iOS and Catalyst then package and compile independently. Platform
failures do not cancel sibling platform jobs; Android and Windows use architecture matrices with fail-fast disabled.

Compile jobs preserve executable permissions and Apple signatures in tar archives. Runtime jobs verify
archive hashes and run those saved applications without installing .NET or rebuilding the sample.
A runtime-only retry can therefore reuse its successful compile job while the app artifact is available.
The summary records **compiled** and **executed** separately from actual step evidence, including a
successful compilation followed by failed execution. iOS simulator execution does not establish
physical-device support; Android ARM64 compilation also does not establish physical-device execution. Catalyst executes directly on macOS, with no simulator.

### Windows-only framework changes

The Windows framework and standalone consumer jobs use a private ZIP-installed .NET SDK under
the runner's temporary directory. This avoids importing Visual Studio's installed Android, Apple
and WebAssembly workloads when installing `maui-windows`. The SDK and workload-set versions come
from `global.json`; the SDK directory alone is cached, keyed by runner OS, host architecture, image
family, SDK/workload versions and setup implementation. No partial cache-key fallback is used.

The package job saves the verified toolchain before its dependent x64 and ARM64 compilation jobs
start. Every installation and cache restore checks the selected executable, SDK and workload-set
versions, the file-based registration, and that only `maui-windows` is installed with no mobile or
WebAssembly workload packs. A bad cache fails verification; changing the action's cache revision
forces a fresh installation. The step summary records cache-hit status and the verified versions.

Consumer NuGet caches, package feeds and build outputs are not part of this cache. The optional
example-package workflow and the lightweight C++/Rust probe keep their existing setup.
The probe does not require a MAUI workload.

Before this change, run `38939526252` spent 9m20s installing workloads for Windows packaging,
5m40s for x64 compilation and 6m40s for ARM64 compilation. Those installs also updated preinstalled
Android, Apple and WebAssembly workloads; the Windows-only platform selection was already correct.

```sh
gh workflow run verify-framework.yml --ref main -f platforms=Windows -f depth=Compile
```

This builds Core once and Windows framework packages once, then compiles both x64 and ARM64 samples.
For interactive desktop verification, use the local steps in [Windows integration](windows-integration.md).

### Android and Apple SDK caches

Android, iOS and Mac Catalyst framework and standalone consumer jobs use the same isolation and
exact-cache approach. Each target has its own cache containing a private archive-installed .NET SDK
and exactly one workload: `maui-android`, `maui-ios` or `maui-maccatalyst`. The private location is
`.tools/dotnet`, matching `eng/dotnet.sh`; host SDKs are not used. Pins come from `global.json`.

Cache keys include the runner OS, host architecture, image family, target, SDK/workload versions and
setup implementation. Apple keys also include the selected Xcode version/build fingerprint. There
are no partial-key fallbacks or cross-target restores. The Android package job saves its verified
cache before the x64 and ARM64 consumers start; Apple jobs save theirs before packaging and compilation
so subsequent runs can reuse them. Every restore checks the executable, versions, installed workload,
file-based registration and absence of unrelated platform packs before any package build begins.

Only the .NET SDK directory is cached. JDK, Android SDK platform/build tools, Xcode and simulator images
retain their existing setup. Consumer NuGet caches and build outputs remain fresh. Combined release
and optional example packaging still use their existing multi-platform bootstrap; runtime-only jobs
continue to execute saved applications without installing an SDK.

### Verify distribution before a release

Use **Verify framework** for everyday development. Use [Verify distribution](../.github/workflows/verify-distribution.yml)
before distributing packages or after changing packaging. Examples remain optional and separate.

After a successful **All / Runtime** framework run, start one distribution workflow at the same commit:

```sh
gh workflow run verify-distribution.yml --ref main -f framework_run=ALL_RUNTIME_RUN_ID
```

The coordinator validates the completed framework run, reuses its Core package, native artifacts and
Windows packages, and assembles the final combined feed once. Seven independent jobs then consume
that same archive through fresh package-only restores:

| Consumer | Compiles | Executes |
| --- | --- | --- |
| Android x64 | Release APK | Android emulator |
| Android ARM64 | Release device APK | No; APK retained for physical-device testing |
| iOS simulator | Release simulator app | iPhone simulator |
| iOS device | Unsigned Release device app | No |
| Mac Catalyst | Release app | No; framework runtime verification is separate |
| Windows x64 | Release app | No desktop execution |
| Windows ARM64 | Release app | No desktop execution |

The **Distribution verification result** rejects missing or failed consumers, mismatched source/run
metadata, and different feed hashes. It retains the compilation and execution outcomes separately in
`distribution-verification`. This workflow never publishes packages, creates releases or tags commits.
Do not move `main` between the prerequisite and distribution run; artifact acceptance requires the same source SHA.

Use **Re-run failed jobs** on the distribution run to reuse successful packaging and consumer jobs.
Jobs have independent deadlines; a consumer failure does not cancel its siblings. Inputs expire after
seven days; expired inputs require a fresh prerequisite run. ARM64 APKs and runtime logs expire after three days.

The standalone [Release / Package gate](../.github/workflows/release-packages.yml) remains available
for packaging-only diagnostics. It accepts the same `framework_run` input, performs the same package
assembly and inspection, and stops before consumers. A successful package gate alone is not a
successful distribution verification.

### Verify the framework sample

The coordinator calls these same workflows. Manual entry points remain available for focused troubleshooting
or checking an optional example. For a successful distribution run, use `package_source=Distribution`
and its run ID; use `Release` for a standalone package gate or `Example` for an example package build:

| Workflow | Evidence |
| --- | --- |
| [Consumers / Android](../.github/workflows/verify-android.yml) | Combined-feed x64 Release compilation and emulator execution, or ARM64 device compilation |
| [Consumers / iOS simulator](../.github/workflows/verify-ios.yml) | Combined-feed Release compilation and simulator execution |
| [Consumers / iOS device compile](../.github/workflows/verify-ios-device.yml) | Unsigned `ios-arm64` Release compilation |
| [Consumers / Mac Catalyst compile](../.github/workflows/verify-catalyst.yml) | Combined-feed Catalyst compilation; direct execution is in Verify framework |
| [Consumers / Windows](../.github/workflows/windows.yml) | Combined-feed compilation for the chosen architecture |

```sh
gh workflow run verify-android.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID
gh workflow run verify-android.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID -f runtime=android-arm64
gh workflow run verify-ios.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID
gh workflow run verify-ios-device.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID
gh workflow run verify-catalyst.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID
gh workflow run windows.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID -f runtime=win-x64
gh workflow run windows.yml --ref main -f package_source=Distribution -f package_run=DISTRIBUTION_RUN_ID -f runtime=win-arm64
```

### Android ARM64 and physical-device verification

Every Android framework run builds the bridge and packages once, then compiles the package-only
sample for `android-x64` and `android-arm64` on separate runners. `Compile` stops there;
`Runtime` additionally runs the saved x64 APK in the emulator. Both compile results are required
by the framework summary and release package gate. Neither result claims physical-device execution.

For a combined release feed or optional example, select `runtime=android-arm64` in
**Consumers / Android**. It compiles without starting an emulator and retains the signed development
APK for three days as `android-Framework-arm64-apk` (or the selected example's name).

To verify the framework on an authorized USB or wireless ARM64 phone, download that exact APK and
use the build run's source SHA. The helper installs the app, runs its channel and native-view checks,
and saves the result, device model/OS, APK SHA-256 and logs under `artifacts/android-devices/`.
It rejects emulator targets and non-ARM64 devices. Keep the phone unlocked and the sample foregrounded.

```sh
gh run download ANDROID_ARM64_CONSUMER_RUN_ID --name android-Framework-arm64-apk --dir artifacts/device-apk
gh run view ANDROID_ARM64_CONSUMER_RUN_ID --json headSha --jq .headSha
adb devices -l
python3 eng/verify-android-device.py DEVICE_SERIAL \
  --apk artifacts/device-apk/dev.nativechannels.sample-Signed.apk \
  --source-commit APK_BUILD_SHA
```

The source SHA is supplied by the caller; the APK hash identifies exactly what was tested.
Record the originating run URL alongside the report. For the framework development feed, the
`framework-app-android-arm64` artifact contains a tar archive and checksum. Download it into
`artifacts/`, check `shasum -a 256 -c artifacts/android-arm64-app.sha256`, extract with
`tar -xzf artifacts/android-arm64-app.tar.gz`, and omit `--apk` to use the extracted default path.
Development-feed execution remains separate from combined-release-feed acceptance.

### Optional examples and deeper checks

Example plugins are optional on **every platform**. [Examples / Package build](../.github/workflows/build-examples.yml)
accepts a successful distribution run (or standalone package gate) and one plugin name. It reuses the retained mobile and
Windows framework packages and native artifacts, then builds only the selected example. Keep the
input run at the same source commit; expired inputs require rebuilding the release package gate.
Choose `framework_source=Distribution` for the coordinator or `Release` for a standalone package gate.

```sh
gh workflow run build-examples.yml --ref main -f framework_source=Distribution -f framework_run=DISTRIBUTION_RUN_ID -f sample=AudioStreamer
gh workflow run verify-ios.yml --ref main -f package_source=Example -f package_run=EXAMPLE_RUN_ID -f sample=AudioStreamer
```

Select Currency, AudioStreamer, VolumeMonitor, or PdfViewer explicitly. Verify that example on its
applicable platforms when changing or releasing it. Framework sample results cover bridge/channel
contracts, not an example's SDK-specific behavior.

[Verify / Lifecycle stress](../.github/workflows/performance.yml) and
[Verify / Benchmarks](../.github/workflows/benchmarks.yml) are separate manual workflows.
[Verify / Native performance](../.github/workflows/native-performance.yml) consumes a successful
same-source distribution feed to measure one native target, with a selectable soak duration.
See [performance measurements](performance.md#measure-real-native-round-trips-and-memory) for commands,
memory metrics and interpretation. The normal
Core suite retains its bounded lifecycle tests. Full local `NativeChannels.slnx` also includes portable
example tests; CI's shared job uses `eng/NativeChannels.Core.slnx`.

### Retries, retention, and cost

Use **Re-run failed jobs** while successful dependencies' artifacts remain available. Core, native
inputs, package feeds, and stage evidence are retained for seven days. Compiled runtime apps and logs
are retained for three days. Expired artifacts require rebuilding the relevant inputs. New source
changes require a new run; do not use old artifacts as evidence for a newer revision.

Jobs and restore/build steps have deadlines and visible build logging. Separate jobs make failures and
retries independent, but runner setup, restore, AOT/linking and queues still take time. Shorter workflow
files do not guarantee shorter builds or lower total runner time. The optional workflows run only when
dispatched. Automatic framework runs cancel obsolete runs on the same event and branch; manual runs
do not cancel an active run. Tags do not start duplicate verification. Verification workflows do not publish packages,
create releases, or tag commits. The separate [NuGet publishing workflow](publishing.md) is manual,
defaults to validation only, and consumes an accepted same-commit distribution feed.

iOS runtime checks give simulator boot and data migration their own five-minute deadline before
starting the five-minute app execution step. Failures retain app logs and simulator diagnostics.
Android AudioStreamer verification requires five fresh process launches to pass; it stops at the
first failure and retains logcat and audio-focus diagnostics. These checks do not retry failed app
assertions or extend the library's request timeouts.

## Native framework changes

`eng/pack-bridges.sh` clears generated Apple binding Release outputs before packing. The Apple SDK
can otherwise retain an old `resources.zip` after files inside an XCFramework directory change.
Always rebuild native frameworks first, pack, inspect the archives, and restore the consumer into
a fresh cache. A successful incremental binding compilation alone does not establish current native assets.

## PDF viewer example

After packing the framework, follow the [PdfViewer build steps](../plugins/Plugin.Maui.PdfViewer/README.md#build-and-verify)
to build its native adapters, pack the local plugin, and restore the separate [sample](../samples/Maui/PdfViewer/README.md).
This exercises native views through a package-only consumer. No publication is needed.
