# VolumeMonitor verification

Nine portable tests passed for initial values, updates, independent subscribers, cancellation,
restart, early exit, disposal, invalid values, and native errors. The restart regression changes the
volume while no listener exists and checks that the next subscription starts with the current level.

## Build verification

On 2026-10-15 the Android adapter and Apple XCFramework compiled, including iOS device, simulator,
and Mac Catalyst slices. This is native compilation, not application runtime verification. The
current preview still requires fresh package-only consumer acceptance on each target. See the
[platform verification record](../../docs/verification.md).

## Physical-device checks on 2026-10-15

Live volume updates were observed on an Android CPH2581 running Android 16 and an iPhone 12 Pro
running iOS 27.0. The iPhone check exposed a stale initial level after Stop, a volume change while
stopped, and Start. The sample now keeps its Apple audio session active while foregrounded and
removes the stopped listener's observer independently.

The user confirmed that Stop → change volume → Start immediately reports the new iPhone level.
The device log recorded 70% before Stop and a fresh 5% initial event after Start, with no event while
stopped. These are manual feature observations, separate from acceptance of the current package artifacts.

The plugin does not set volume or configure the host's Apple audio session. The sample owns that
session, stops listening and deactivates it on backgrounding, and reactivates it without automatically
resuming listening when the app returns.

## Remaining checks

### Mac Catalyst correction on 2026-10-17

Manual testing found that the previous `AVAudioSession.outputVolume` observer stayed at 100%
while Mac system volume changed. Its subscription/lifecycle checks passed, but that did not establish
system-volume notification delivery. Catalyst now uses Core Audio default-output-device and scalar
volume listeners; the iOS observer is unchanged.

The replacement compiled for arm64 and x64 Catalyst and all iOS slices. Local package inspection,
nine portable tests, and the rebuilt Release Catalyst consumer's subscription checks passed. Its
initial reading was 59.15%, matching the Mac's approximately 59% setting. The user confirmed live
Mac volume updates and an immediate current reading after Stop/change/Start. Output-device switching
remains unverified. This was a local package rebuild, not acceptance of the latest combined CI feed.

### Outstanding coverage

- Fresh package-only Release execution of the current preview on each target.
- Physical-device background/resume after the audio-session fix and independent iOS observer counts.
- Bluetooth/headphone routes, interruptions, broader Android OEM behavior, and Catalyst output-device switching.
- Windows endpoint notifications and cleanup; see [Windows plugin verification](../../docs/windows-plugins-verification.md).

Simulator initial readings do not establish physical-button notification delivery. No packages are published.
