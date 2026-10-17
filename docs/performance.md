# Performance and lifecycle stress

The repository includes repeatable measurements and lifecycle checks. Performance numbers
describe the measured host and workload; they are not a native audio latency guarantee or a device benchmark.

## Run the benchmarks

The separate `benchmarks/NativeChannels.Benchmarks` console project uses
[BenchmarkDotNet](https://benchmarkdotnet.org/articles/guides/getting-started.html). It is outside the
portable solution, so ordinary `dotnet test` does not restore or run the benchmark dependency.

```sh
bash eng/bootstrap.sh
bash eng/benchmark.sh
```

The default is a short run of every benchmark. For focused measurements:

```sh
bash eng/benchmark.sh --filter '*CodecBenchmarks*' --job short --exporters json
bash eng/benchmark.sh --filter '*ChannelBenchmarks*' --job short --exporters json
```

Use `--job dry` to check that every benchmark executes. Dry runs are smoke tests and do not provide
useful steady-state timing. For publication-quality measurements, use `--job default`, an idle machine,
and repeated runs on the same host. Keep the generated environment header and source revision with
the results in `artifacts/benchmarks/`. Do not compare noisy hosted-runner timing against a fixed CI threshold.

| Benchmark | What it measures |
| --- | --- |
| Codec encode/decode | JSON, standard binary, and raw binary codecs with 256 B, 4 KiB, and 64 KiB byte arrays |
| Method round trip | Typed Int32 reply through both managed messenger endpoints |
| Reentrant round trip | A remote handler calls back into the caller before replying |
| Binary round trip | A 4 KiB message through BasicMessageChannel and BinaryMessageCodec |
| Event delivery | A new subscription delivering 64 values, reported per value including amortized setup/cleanup |

Channel benchmarks compare the ordinary 30-second timeout with an explicit infinite timeout.
They use `InMemoryTransport`; JNI, Objective-C marshaling, native dispatch queues, mobile AOT, and
UI rendering are not included. `[MemoryDiagnoser]` reports managed allocations, not native heap use.
Retain buffer copies at ownership boundaries unless measurements and lifetime tests justify a change.

## Measure real native round trips and memory

ChannelExplorer has an opt-in Release profile that uses its registered Kotlin, Swift or Windows
adapter through package references. Its normal functional checks run first. Profiling is disabled
unless `NativeChannelsProfileSeconds` is set; normal sample runs and pull-request jobs stay unchanged.

After [building the local packages and sample adapters](building.md), run one target:

```sh
python3 eng/profile-native.py catalyst --seconds 300
python3 eng/profile-native.py android --device ANDROID_SERIAL --runtime android-arm64 --seconds 300
python3 eng/profile-native.py ios --device SIMULATOR_UDID --seconds 300
```

Use `android-x64` for an x64 emulator. The iOS command targets an already booted simulator;
it does not provision or sign a physical iPhone application. The runner never starts an emulator.
Run one target at a time locally. On a Windows desktop with the pinned SDK and MAUI workload,
place the final combined framework feed in `artifacts/packages`, then run:

```powershell
python eng/profile-native.py windows --seconds 300
```

The runner restores into a fresh package cache, checks that framework archives match the local feed,
and builds only the selected Release sample. It assigns a unique run ID, launches once, stops its
application when finished, and rejects stale, incomplete, failed, or wrong-platform reports.
Restores, builds and execution have separate deadlines. Use 300–1800 seconds for a soak;
a shorter run validates the harness rather than long-duration stability.

| Measurement | Workload |
| --- | --- |
| `method-int32` | C# → native `echo` → C#, tagged JSON Int32 |
| `method-reentrant` | C# → native → managed `getToken` → native → C# |
| `binary-reentrant-4096` | 4 KiB raw binary message, including a native-to-managed message and its reply |
| Lifecycle soak | Repeated handle disposal, reentrant methods/messages, live subscription cancellation, cooperative request cancellation and native view recreation |

Each timing has 32 warmup operations and 256 measured operations, reporting min, mean, p50, p95 and
max in milliseconds. Percentiles use the nearest-rank rule. Calls are sequential, include reply
validation, and measure application-visible round trips. This is neither saturated throughput nor
audio-device latency. The Windows sample measures its managed Windows adapter and WinUI view;
it does not benchmark the optional C++/Rust C ABI.

The soak checks that native subscriptions return to zero each cycle, paces cycles by 100 ms,
recreates a view every ten cycles, and samples memory about once per second. It ends with a
five-second idle observation, without forcing GC. Each sample includes managed retained bytes and
cumulative managed allocated bytes, plus platform counters returned through the native adapter:

| Platform | Native/OS counters (bytes) |
| --- | --- |
| Android | Process PSS (`Debug.getPss`, converted from KiB), native allocated heap, JVM used heap |
| iOS / Catalyst | Mach `TASK_VM_INFO` physical footprint and resident size |
| Windows | Process working set and private bytes |

Process counters include runtime, managed heap and resources; they do not isolate NativeChannels
allocations. These metrics have different meanings across operating systems and must not be compared
as equivalent heap sizes. See [Android Debug](https://developer.android.com/reference/android/os/Debug),
[Apple task information](https://github.com/apple-oss-distributions/xnu/blob/main/osfmk/mach/task_info.h),
and [.NET process memory](https://learn.microsoft.com/dotnet/api/system.diagnostics.process.privatememorysize64).

Reports go to `artifacts/native-performance/<run-id>/`: build/process logs, input hashes, raw JSONL
records and `summary.json`. The summary retains source revision, dirty-tree status, package and sample
adapter hashes, runtime/OS, timings, cycle count, and first/last/peak/delta memory. Positive retention
can be caching or delayed collection; a flat short run cannot establish absence of leaks. Compare
repeated runs on the same host, duration and configuration; investigate trends with platform profilers.
No unstable hosted-runner latency threshold is used as a release gate.

### Optional runtime workflow

**Verify / Native performance** accepts a successful **Verify distribution** run at the same source
commit, one target (Android emulator, iOS simulator or Catalyst), and a 60, 300 or 900-second duration.
It reuses the verified combined feed and adapters; Core/native prerequisites are not rebuilt.
Only the selected sample is compiled and executed. Results expire after seven days. There is no
automatic schedule, pull-request trigger or package publication.

```sh
gh workflow run native-performance.yml --ref main \
  -f package_run=DISTRIBUTION_RUN_ID -f platform=catalyst -f seconds=300
```

Windows desktop execution is a local command, not part of this hosted runtime workflow. Native
profiling implementation, compilation and execution evidence are recorded separately in
[verification](verification.md); adding the workflow does not establish that every target ran.

## JSON buffer ownership

The JSON encoder writes to an owned `ArrayBufferWriter<byte>`. Returned memory retains its backing
array and is never pooled or reused by another invocation. Spare capacity can increase retained
memory beyond payload size, so report both allocation traffic and retained heap size when measuring.
Concurrent ownership tests and protocol fixtures protect those semantics. Record source revision,
host, runtime, workload, iterations, allocations, and uncertainty for any published performance claim.

## Repeat the lifecycle tests

```sh
bash eng/stress.sh 20
```

Each repetition verifies 1,024 reentrant requests across shared handles, 200 handler replacements
and channel-kind reuse cycles, 640 pending requests cancelled by concurrent disposal, 320 independent
event subscriptions, and 200 managed view-channel connections. Tests use completion signals to
coordinate handler entry and teardown. Timeouts are watchdogs, not scheduling assumptions.
TRX results go into a new directory under `artifacts/stress/` for each invocation.

These tests also run once during ordinary portable tests. Repetition is manual; there is no scheduled
stress job. The manual **Verify / Lifecycle stress** workflow runs 20 repetitions. The separate
**Verify / Benchmarks** workflow runs the short benchmark suite. Its artifacts expire after seven days. It does not publish packages.

The package-only MAUI sample additionally performs 32 native view disconnect/reconnect cycles,
checking fresh identities, cancellation, stale-channel rejection, callbacks, and a second independent
view. Android, iOS, and Catalyst verification scripts require its completion marker. This checks
functional cleanup; it does not prove the absence of long-duration native memory leaks.

See [release checklist](release-checklist.md) for remaining platform and release requirements.
