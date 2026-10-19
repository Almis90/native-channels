# AudioStreamer verification

Ten portable tests cover PCM generation and chunk continuity, binary framing, backpressure,
cancellation, disposal/restart, bridge ownership, malformed replies, startup failure recovery,
framework-neutral registration and DI ownership. They passed locally on 2026-10-17.

## Avalonia Windows — 2026-10-17

The [Avalonia sample](../../samples/Avalonia/AudioStreamer/README.md) was cross-built in Release
with .NET SDK 10.0.401 on Ubuntu, then executed on Windows 11 build 26200, x64, using the real
default audio endpoint. Four local archives supplied Core, the Windows transport, the Avalonia
host and AudioStreamer; archive hashes matched the isolated NuGet cache. The consumer has no
library project references, MAUI dependency or Windows App SDK dependency.

The final self-contained application's silent verification passed native progress, bounded
buffering, pause/resume, cancellation, exact drain counts and restart. Three additional host
sessions were disposed from a worker thread while paused writes were pending; their DI-owned
streamers closed and fresh sessions reopened successfully. The sample exercised playback
controls and minimize/restart, then closed its actual window with paused playback active.
Exit code was zero and exactly one completed-cleanup marker was recorded. The five existing
Windows AudioStreamer tests also passed against the new plugin package, including hardware
playback, lease cancellation, stale PCM rejection and module disposal.

The real UI was also checked at zero signal level: Loop, Pause, Resume, Stop, a complete
16-second play (1,536,000 PCM bytes), empty-buffer recovery and window close all worked.

Local logs, application hashes and native test results are under `artifacts/audio-avalonia/windows/`.
No workflows ran and no packages were published. This establishes Windows Avalonia execution;
audible quality, routing and physical latency remain unverified.

## Avalonia Linux — 2026-10-17

The [Linux sample](../../samples/Avalonia/AudioStreamer/Linux/README.md) was built and run
on Ubuntu 24.04.5 x64, kernel 6.8.0-142, with .NET SDK 10.0.401, ALSA 1.2.11 and PulseAudio
16.1. VirtualBox supplied an Intel HDA output with its Linux driver; tests used the real
ALSA default output through PulseAudio, not an ALSA null device. The Avalonia window ran
under Xvfb with Openbox on X11.

Four fresh local packages supplied Core, the Linux transport, the Avalonia host and the
AudioStreamer C++ ALSA adapter (`libnc_audio.so`). Package inspection and audit passed. The runtime verifier
confirmed that published assemblies and the native library matched those archives and that the package-only
consumer had no MAUI or Windows App SDK dependency.

After moving Linux playback/session handling into C++, all six Linux integration tests
passed, including cancellation, disposal, single-frame playback and idle underrun recovery.
Native C++ tests passed partial-write preservation, native buffer accounting, backpressure,
pause, failure recovery, cancellation, destruction and codec checks under AddressSanitizer
and UndefinedBehaviorSanitizer. All 171 packaging checks passed on Linux. The unchanged
portable AudioStreamer facade had also passed its ten tests earlier on the same date.
The actual Avalonia application passed native silent playback, bounded buffering,
pause/resume, exact drain counts, cancellation, repeated DI session recreation,
minimize/restart and closing with paused playback active. It exited successfully with
exactly one completed-cleanup marker. The startup UI was also visually checked.

Evidence, source/package/application hashes and logs are retained locally under
`artifacts/audio-avalonia/linux/run-fea3c1f9896c436fa18d30e1d85ced21/` and
`artifacts/linux-cpp/`. The C++ build used GCC 13.3. No workflows ran and no packages were published.
This verifies native playback timing and lifecycle on this VM; physical audibility,
Bluetooth routing, latency, PipeWire, Wayland, ARM64 and outputs without native pause
support are not established by this run.

## Build verification

On 2026-10-15 the Android adapter and Apple XCFramework compiled, including iOS device, simulator,
and Mac Catalyst slices. This is native compilation, not application runtime verification. The
current preview still requires fresh package-only consumer acceptance on each target. See the
[platform verification record](../../docs/verification.md).

## Manual playback observations — 2026-10-15

**Audible music was confirmed on both the Android emulator and iOS simulator.** Phrase completion,
looping, pause/resume, and Stop returning the buffer to `0 / 500 ms` were checked on both. The iOS
check also confirmed backgrounding stopped playback and left an empty buffer on return.

The “Sunny Steps” demo uses 50 ms PCM chunks. Automated native checks use silent PCM to measure
progress, bounded occupancy, cancellation, drain counts, and restart; they do not establish audibility.
Manual observations are recorded separately from package acceptance, acoustic quality, or latency measurements.

## Remaining checks

- Fresh MAUI package-only Release execution after the dependency change on Android, iOS, Windows, and Catalyst.
- Physical Android/iOS playback, Bluetooth/headphone routing, and interruption stress.
- Hardware end-to-end latency and long-running glitch-free playback.
- Windows native API and installer acceptance described in [Windows plugin verification](../../docs/windows-plugins-verification.md).

Background audio is outside this foreground example. Nothing is published by the build scripts.
