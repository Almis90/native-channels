# AudioStreamer verification

Eight portable tests cover PCM generation and chunk continuity, binary framing, backpressure,
cancellation, disposal/restart, bridge ownership, malformed replies, and startup failure recovery.
They passed in the current source-tree checks.

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

- Fresh package-only Release execution for the current preview on Android, iOS, Windows, and Catalyst.
- Physical Android/iOS playback, Bluetooth/headphone routing, and interruption stress.
- Hardware end-to-end latency and long-running glitch-free playback.
- Windows native API and installer acceptance described in [Windows plugin verification](../../docs/windows-plugins-verification.md).

Background audio is outside this foreground example. Nothing is published by the build scripts.
