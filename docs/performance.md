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
