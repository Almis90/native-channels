# AudioStreamer sample

A separate MAUI consumer of the local `Plugin.Maui.AudioStreamer` reference package. It generates a
16-second original instrumental loop in C#, sends PCM chunks through BasicMessageChannel, and plays
them with Kotlin AudioTrack or Swift AVAudioEngine. Choose Play for one pass or Loop for continuous music. It exposes signal level, pause/resume, stop,
native buffer occupancy, and mean enqueue wait.

See the [plugin guide](../../../plugins/Plugin.Maui.AudioStreamer/README.md) for build/run instructions,
format, backpressure, and lifecycle. The app references the plugin package and `NativeChannels.Maui`;
its native adapters arrive transitively. Android requires API 31+.
The [Avalonia Windows sample](../../Avalonia/AudioStreamer/README.md) reuses the music generator,
native verification scenarios, and plugin without a MAUI dependency.

Startup silently verifies native playback, queue bounds, pause/resume, cancellation, drain, and a clean
restart. It logs `AUDIO_SAMPLE PASS …` and writes `audio-result.txt` in application data. The foreground
app stops playback when backgrounded. System media volume still applies to the music. The default signal level is 16%; if playback is quiet, raise the device’s media volume.

“Sunny Steps” is an original eight-bar melody with piano-style notes, bass, and a soft beat, synthesized
by `MusicGenerator` at 120 BPM. Its score and synthesis code are part of this MIT-licensed repository;
there is no downloaded recording, third-party song, streaming service, or additional asset license.
The demo sends 50 ms PCM chunks to leave headroom for emulator bridge latency while retaining the
native half-second buffer limit.
