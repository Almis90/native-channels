# Plugin.Maui.AudioStreamer

A foreground audio reference plugin demonstrating useful binary transfer with NativeChannels. C# generates
PCM chunks; Kotlin plays them with `AudioTrack`, and Swift plays them with `AVAudioEngine` and
`AVAudioPlayerNode`. Windows uses WinMM's `waveOut` PCM queue. The separate MAUI app references
**only the local plugin package**.

The example is for plugin authors and has not been published to NuGet.org. It requires Android 12+
(API 31), iOS/Mac Catalyst 15+, or Windows 10 build 17763+. See [verification](VERIFICATION.md)
for actual execution results.

On Windows, run `./eng/verify-windows.ps1 -Plugin AudioStreamer` from the repository root.
This builds the Windows package, runs real-device tests with silent PCM, and executes the sample.
Use `-BuildOnly` on machines without an audio endpoint; it excludes hardware tests.
Playback uses the default output selected when the session opens. Reopen after an output-route
change. Played-frame counts advance when Windows returns completed buffers, so progress is
reported in submitted chunk increments. Pause preserves queued audio; stop, lease cancellation
and disposal reset the device and release the queued buffers. Audible quality remains a manual check.

## Try it

Build the framework first using the repository's [build guide](../../docs/building.md), then:

```sh
# From the repository root, with JAVA_HOME and ANDROID_HOME configured.
native/android/gradlew -p native/android --no-daemon :audio:assembleRelease
python3 plugins/Plugin.Maui.AudioStreamer/eng/build-apple.py
bash plugins/Plugin.Maui.AudioStreamer/eng/pack.sh
python3 plugins/Plugin.Maui.AudioStreamer/eng/inspect-package.py

bash eng/restore-audio-sample.sh -p:TargetFrameworks=net10.0-android -r android-arm64
bash eng/dotnet.sh build samples/Maui/AudioStreamer -c Release -f net10.0-android -r android-arm64 --no-restore
bash eng/verify-android-audio.sh EMULATOR_SERIAL Release android-arm64
```

For iOS, restore/build with `-p:TargetFrameworks=net10.0-ios -r iossimulator-arm64` and run
`bash eng/verify-ios-audio.sh SIMULATOR_UDID Release`. Each restore cleans the sample's outputs and
uses a new package cache. Use `android-x64` instead of `android-arm64` on x64 emulators.

The app first runs silent PCM through the real native player to check playback progress, bounded
buffering, pause/resume, cancellation, drain, and clean restart. Then choose **Play 16-second music**
or **Loop music**. Adjust signal level, pause/resume, and stop. The display shows
native buffer occupancy and mean enqueue wait. System media volume controls audible loudness too.

## Use the facade

Following the Currency reference plugin's packaging pattern, configure the local feed and reference
`Plugin.Maui.AudioStreamer`. Native adapters and framework dependencies arrive transitively.
Register it once alongside your other modules:

```csharp
builder.UseNativeChannels(options => options.AddAudioStreamer());
builder.Services.AddAudioStreamer();
```

Inject `IAudioStreamer`:

```csharp
await using var playback = await audio.OpenAsync(cancellationToken);
await foreach (var pcm in GeneratePcmAsync(cancellationToken)) // Your source of PCM16 chunks.
{
    await playback.WriteAsync(pcm, cancellationToken);
}
await playback.DrainAsync(cancellationToken);
```

`PauseAsync` retains buffered audio. `ResumeAsync` continues it. `GetStatusAsync` reports state,
buffered/capacity frames, and played frames. `DisposeAsync` stops, flushes, and releases the session;
open a new session to restart. One session is allowed per streamer/native bridge at a time. The DI
container owns the streamer service; callers own and must dispose the sessions they open.

`WriteAsync` returns when the entire chunk has been accepted by the native queue. It **does not mean
the sound has reached the speaker**. Await writes sequentially. `DrainAsync` waits for native playback
progress after your final write; while paused it waits for resume or cancellation. No timer paces the
producer: the bounded buffer provides backpressure. Cancel writes/drains or dispose the session to stop waiting.

## Audio and message contract

| Property | Contract |
| --- | --- |
| Audio format | Signed PCM16, little-endian, mono, 48,000 frames/second |
| Chunk size | 1–4,800 whole frames (2–9,600 PCM bytes); sample uses 960 frames / 20 ms |
| Native capacity | At most 24,000 submitted PCM frames / 500 ms, including queued and scheduled frames |
| Data channel | `plugin.maui.audio.pcm`, BasicMessageChannel + BinaryMessageCodec |
| Control channel | `plugin.maui.audio.control`, MethodChannel |
| Data payload | 16 opaque session-ID bytes followed by raw PCM; the binary codec adds its null/present marker |
| Binary reply | One byte: 0 accepted, 1 full, 2 paused, 3 stale/closed session, 4 malformed PCM |

A full or paused response guarantees **zero frames accepted**, so C# can retry that chunk. Cancellation
or a lost reply can race acceptance; the facade never automatically retries those ambiguous outcomes.
Each new session gets a new ID. Late chunks from an earlier session are rejected rather than played.
JSON is used only for controls/status and the initial session ID, never for the audio chunks.

The `open` control request remains pending with an infinite timeout as a native resource lease.
Cancelling that request or disposing the containing bridge releases native audio resources even if an
application forgets to send `stop`. Start-up is bounded to five seconds and explicit stop to two seconds.

Android uses nonblocking writes on a dedicated worker and handles partial writes. Buffer occupancy
includes both the application queue and frames already submitted to AudioTrack. The playback head
is tracked across its unsigned 32-bit wrap. Android 12+ allows an explicit one-frame start threshold,
so even a short PCM buffer can start without filling the track's entire buffer. Audio focus loss ends
the session with `audio_interrupted`.

Apple converts PCM16 to the engine's mono float format and schedules buffers with `.dataPlayedBack`
callbacks. Occupancy decreases when a whole buffer finishes, so it is a buffer-level estimate.
Interruption, removal of the output route, or engine reconfiguration ends the session; the app must
start again. The example owns the app's AVAudioSession while playing and deactivates it on close.
Coordinate this with other audio features before adapting it for a shared production audio session.

## Boundaries and measurements

This is **foreground playback**. The app stops and flushes when its window stops/backgrounds. It does
not request microphone access, use a foreground service, or enable iOS background audio. Background
playback needs a separate native lifecycle/source design and is intentionally not implemented here.

`BinaryMessageCodec` is a message codec, not an audio decoder. WAV headers, MP3, AAC, stereo, and other
sample rates are rejected by this fixed contract or would be interpreted as incorrect PCM. Convert
your source to the documented format before sending it.

The displayed enqueue wait includes native-buffer backpressure and bridge scheduling. It is not
end-to-end audio latency. Simulator clock progress does not prove audible quality, glitch-free
playback, Bluetooth behavior, or production real-time suitability. Physical-device latency, underruns,
and route stress need measurement before making those claims.

## Source and validation

- [Managed facade](src/Plugin.Maui.AudioStreamer/AudioStreamSession.cs): resource lease, framing, retry rules, and disposal.
- [Kotlin adapter](native/android/src/main/java/dev/nativechannels/audio/AudioStreamerModule.kt): AudioTrack, focus, bounded queue, partial writes.
- [Swift adapter](native/apple/Sources/AudioStreamerModule.swift): AVAudioEngine scheduling, playback acknowledgements, interruptions.
- [Windows adapter](native/windows/WindowsAudioStreamerModule.cs): waveOut PCM playback and queue ownership.
- [Interactive sample](../../samples/Maui/AudioStreamer): original PCM music synthesis and playback controls.
- [Portable tests](../../tests/Plugin.Maui.AudioStreamer.Tests): exact PCM, backpressure, cancellation, lease cleanup, and malformed replies.

Run `bash eng/dotnet.sh test -c Release` for portable tests. Package inspections check the generic-bridge
dependency, adapter archives, provider manifest, and transitive imports; fresh native consumer scripts
check actual loading and playback progress. [Verification](VERIFICATION.md) separates implementation,
compilation, execution, and remaining gaps.

Platform references: [Android AudioTrack](https://developer.android.com/reference/android/media/AudioTrack),
[Apple AVAudioPlayerNode](https://developer.apple.com/documentation/avfaudio/avaudioplayernode), and
[AVAudioSession](https://developer.apple.com/documentation/avfaudio/avaudiosession).
