# AudioStreamer with Avalonia on Linux

This Linux desktop head reuses the Windows sample's Avalonia UI, music generator,
verification scenarios and managed audio facade. `Plugin.AudioStreamer` supplies
a C++ Linux module linking the system **ALSA** library; the application consumes four local
NuGet packages and has no MAUI dependency.

## Requirements

- .NET SDK pinned in the repository's `global.json`, Python 3, and the usual
  Avalonia desktop dependencies. The local scripts use `.tools/dotnet`.
- Linux x64, currently verified on Ubuntu 24.04 with X11.
- To build the native plugin: `g++`, `pkg-config`, and `libasound2-dev`.
- `libasound.so.2` and a working ALSA `default` playback output with pause support.
  On Ubuntu, the tested desktop path is `libasound2t64`, `libasound2-plugins`, and a
  running user PulseAudio server connected to an audio device. Existing PipeWire
  ALSA configurations may work, but have not been verified here.

The adapter does not install, start or reconfigure an audio server. A server-only VM
needs a virtual sound card, its Linux driver and a user audio session before testing.
Run `aplay -l` and, for PulseAudio, `pactl list short sinks` to inspect the output.
Do not use an ALSA null device to claim real playback verification.

An output without native pause support reports `audio_pause_unsupported`. The adapter
does not silently discard buffered PCM or substitute a software playback clock.
Missing libraries and device/server failures are reported as structured channel errors.

## Build and run locally

From the repository root, preserve any existing `artifacts/packages` feed before
switching platforms. The build script requires an empty feed, creates Linux-scoped
packages, checks their dependencies and native assets, restores an isolated sample
cache, and publishes a self-contained Release application:

```sh
bash eng/build-audio-avalonia-linux.sh
artifacts/audio-avalonia/linux/app/AudioStreamer.Linux
```

Choose Play for one 16-second instrumental, or Loop for continuous playback. Pause
retains queued audio; Stop and minimize flush it. Restoring the window does not restart
playback. Closing awaits playback cleanup, the DI session, and the Avalonia host.

On an X11 desktop, run the silent acceptance check:

```sh
python3 eng/verify-audio-avalonia-linux.py
```

For a VM without a desktop, install Xvfb and Openbox, and keep the audio session running:

```sh
xvfb-run -a -s '-screen 0 1280x1024x24' sh -c \
  'openbox >/tmp/audio-openbox.log 2>&1 & exec python3 eng/verify-audio-avalonia-linux.py'
```

The verifier requires native playback, exact drain counts, bounded buffering,
pause/resume, cancellation, repeated host disposal/recreation, minimize/restart and
closing a window with paused playback active. It checks exit status, the final result,
one completed-shutdown marker and the published assemblies/C++ library against the local archives.
Logs, source and package hashes, and OS/library versions are saved under
`artifacts/audio-avalonia/linux/run-*/`.

Run the adapter tests separately with the same audio session:

```sh
bash eng/dotnet.sh test tests/Plugin.AudioStreamer.Linux.Tests -c Release
bash eng/test-linux-native.sh
```

Hardware-free queue/error tests are C++ tests run by `eng/test-linux-native.sh`, with
address/undefined-behavior sanitizers. The .NET integration suite can exclude audio hardware
with `--filter 'Category!=AudioHardware'`.
These scripts run locally and never dispatch workflows or publish packages.

## Native buffering

The module accepts at most 24,000 unplayed frames across its software queue and ALSA.
A C++ worker pumps nonblocking native writes and retains unwritten suffixes after
partial writes or `EAGAIN`. Played-frame counts come from ALSA's reported delay and
underrun state. Small chunks start explicitly so even a single-frame write can drain.
Idle underruns prepare the device for the next write; server/device failures terminate
the pending native lease and release the PCM handle. Stop drops PCM immediately,
including while paused.

All audio/session logic lives in C++. The shared [Linux bridge](../../../../native/linux/README.md)
only forwards channel requests and replies through a C ABI; it contains no ALSA bindings.

The PCM format and binary protocol are identical to the [plugin contract](../../../../plugins/Plugin.AudioStreamer/README.md).
Native progress does not establish audible quality, physical latency or output-route
behavior. See [execution results](../../../../plugins/Plugin.AudioStreamer/VERIFICATION.md)
and the [ALSA PCM API](https://www.alsa-project.org/alsa-doc/alsa-lib/group___p_c_m.html).
