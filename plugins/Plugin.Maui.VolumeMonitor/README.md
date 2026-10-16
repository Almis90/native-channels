# Plugin.Maui.VolumeMonitor

An EventChannel reference plugin that streams the initial media/output volume and subsequent changes
as doubles from 0 to 1. A separate MAUI sample shows an animated volume bar, recent events, and
start/stop controls. This is a source reference example with local NuGet packaging, not a published package.

## Use the plugin

Register the packaged native module and managed facade:

```csharp
builder.UseNativeChannels(options => options.AddVolumeMonitor());
builder.Services.AddVolumeMonitor();
```

Inject `IVolumeMonitor`, then enumerate with a cancellation token:

```csharp
await foreach (var volume in monitor.ListenAsync(cancellationToken))
{
    Console.WriteLine($"Volume: {volume:P0}");
}
```

Every enumeration owns a separate EventChannel handle and native observer. Cancellation, breaking out
of the loop, or disposing the containing NativeChannels session ends that subscription. Other listeners
remain active. Streams have no request timeout; cancel them when the feature is no longer visible.
Update UI on the main thread if the consuming code runs on a background context.

The facade uses the existing channel API directly:

```csharp
await using var channel = channels.GetEventChannel("plugin.maui.volume");
await foreach (var volume in channel.ListenAsync<double>(cancellationToken: cancellationToken))
{
    // Initial normalized level, followed by changes.
}
```

Events are finite `Double` values in `[0, 1]`; invalid native values fail the facade with
`ChannelSerializationException`. Native read/registration failures end the stream with the structured
`volume_unavailable` error. Consecutive identical native readings are suppressed. The first reading
is always emitted, including zero. EventChannel's default bounded buffering/overflow behavior applies.

## Native behavior and limitations

**Windows (10 build 17763+):** Core Audio observes the default multimedia render endpoint.
Each subscription registers its own endpoint-volume and default-device callbacks, reads the initial
level, follows default-device changes, and unregisters on cancellation or disposal. Notifications
are coalesced and duplicate levels suppressed. The scalar is the endpoint's volume setting, not
measured loudness; muting does not change this value. An unavailable endpoint ends the stream with
`volume_unavailable`; restart listening after an output device becomes available.

Run `./eng/verify-windows.ps1 -Plugin VolumeMonitor` to test the packaged adapter and sample.
One hardware test changes output volume by two percentage points and restores the original value
in `finally`, checking delivery to two listeners. The plugin itself never changes volume.
`-BuildOnly` excludes hardware tests for CI machines without an output endpoint.

**Android (API 24+):** Kotlin registers a `ContentObserver` on `Settings.System.CONTENT_URI` and reads
`AudioManager.STREAM_MUSIC` on each notification. The result is `currentIndex / maximumIndex`, not a
measurement of loudness or decibels. Registration happens before the initial read. Notifications for
unrelated settings cause a read but do not emit a duplicate volume. Cleanup unregisters on the same
main looper; queued callbacks ignore closed subscriptions.

This is best-effort observation: settings notifications may be delayed/coalesced or absent on some
devices/routes. It does not use the internal `VOLUME_CHANGED_ACTION` broadcast or polling. It needs
no runtime permission and does not write settings or change volume. The sample directs its foreground
volume keys to the music stream; the plugin itself does not change activity settings.

**Mac Catalyst (15+):** Core Audio observes the default output device's volume and follows
default-device changes. Each subscription owns its device and volume listeners and removes both
on termination. It reads the master scalar where available, otherwise the average of readable
preferred stereo channels (falling back to channels 1 and 2). Outputs without a readable volume
control end the stream with `volume_unavailable`; reconnect an adjustable output and restart.
This reports the volume setting, not measured loudness; mute is separate. The Mac implementation
does not use or activate `AVAudioSession`.

**iOS (15+):** Swift retains a separate `NSKeyValueObservation` for
`AVAudioSession.outputVolume`, with `.initial` and `.new`. Each token is invalidated on termination.
Registration, event emission, and cleanup are serialized on the main queue.

The plugin observes the app's existing audio session and never activates/deactivates it or changes its
category. The host must manage the audio session lifecycle as appropriate to its audio features. The
sample keeps an ambient session active while in the foreground, including between Start/Stop cycles,
so iOS can keep its volume state current. Stop removes the subscription's observer; backgrounding
also deactivates the audio session. Returning activates the session without restarting the listener.
Do not copy the sample's session ownership into an app that already has an audio
session manager; integrate observation with that manager instead.

Volume change delivery on iOS Simulator is not a reliable acceptance test. The sample labels this
limitation, and [verification](VERIFICATION.md) records initial-value/lifecycle checks separately from
real hardware changes. Output volume is not every button press: pressing Up at maximum volume may
produce no event. Route, mute, and hardware behavior need physical-device testing. This example
provides foreground observation, not a background service or audio playback implementation.

## Build and run locally

Build the framework using the repository's [build guide](../../docs/building.md), then:

```sh
# From the repository root, with JAVA_HOME and ANDROID_HOME configured.
native/android/gradlew -p native/android --no-daemon :volume:assembleRelease
python3 plugins/Plugin.Maui.VolumeMonitor/eng/build-apple.py
bash plugins/Plugin.Maui.VolumeMonitor/eng/pack.sh
python3 plugins/Plugin.Maui.VolumeMonitor/eng/inspect-package.py

bash eng/restore-volume-sample.sh -p:TargetFrameworks=net10.0-android -r android-arm64
bash eng/dotnet.sh build samples/VolumeMonitor.Sample -c Release -f net10.0-android -r android-arm64 --no-restore
bash eng/verify-android-volume.sh EMULATOR_SERIAL Release android-arm64
```

Use `android-x64` for x64 emulators. For iOS, restore/build with `-p:TargetFrameworks=net10.0-ios
-r iossimulator-arm64`, then run `bash eng/verify-ios-volume.sh SIMULATOR_UDID Release`.
Run one emulator at a time. The consumer has only a plugin package reference; native adapters and
framework dependencies arrive transitively through the local package. Fresh caches and archive hashes
verify the packaging. These commands do not publish anything to NuGet.org.

The app checks initial values, independent listeners, cancellation and restart, then starts listening.
Use system volume controls to see the percentage, animated bar, and last 12 events update. Stop removes
the observer; Start creates a fresh one. Backgrounding stops observation, and returning requires Start.

## Source and validation

- [Managed facade](src/Plugin.Maui.VolumeMonitor/VolumeMonitor.cs)
- [Kotlin observer](native/android/src/main/java/dev/nativechannels/volume/VolumeMonitorModule.kt)
- [Swift observer](native/apple/Sources/VolumeMonitorModule.swift)
- [Windows observer](native/windows/WindowsVolumeMonitorModule.cs)
- [Sample app](../../samples/VolumeMonitor.Sample)
- [Portable tests](../../tests/Plugin.Maui.VolumeMonitor.Tests)
- [Recorded verification](VERIFICATION.md)

Platform references: [AudioManager](https://developer.android.com/reference/android/media/AudioManager),
[ContentObserver](https://developer.android.com/reference/android/database/ContentObserver), and
[Apple outputVolume/KVO](https://developer.apple.com/documentation/avfaudio/avaudiosession/outputvolume).
