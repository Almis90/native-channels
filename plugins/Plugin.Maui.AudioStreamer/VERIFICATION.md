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
audible quality, routing, physical latency and other Avalonia targets remain unverified.

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
