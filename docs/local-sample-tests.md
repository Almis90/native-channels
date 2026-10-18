# Local Avalonia sample tests

`eng/test-samples.py` packages the current source, audits the packages, builds the five
Avalonia consumers, and runs their native integration checks. It does not invoke a
workflow, push commits, or publish packages. Each invocation writes a new directory
under `artifacts/local-samples/<platform>/`, including `summary.json`, per-sample logs,
binary hashes, and build provenance. A failure or timeout returns a nonzero exit code;
the runner continues through the remaining samples. Build failures stop before execution.

Start on Windows with Python 3.11+ and the SDK pinned by `global.json`:

```powershell
python eng/test-samples.py --platform windows
```

The runner discovers `.tools/dotnet/dotnet.exe` (or `.tools/dotnet/dotnet` on Unix),
then falls back to `dotnet` on PATH. Use `--dotnet /absolute/path/to/dotnet` to override.
Windows needs an interactive desktop and an available output audio device.
The apps open and close automatically; leave them uninterrupted during the run.

## Linux VM

Run inside the VM after syncing the source. Install the dependencies described in
[sample setup](../samples/Avalonia/README.md), including both Qt PDF and GTK/Poppler backends. Audio needs
ALSA and a running PulseAudio server with an output sink. The PDF suite runs **both**
LinuxQt and LinuxGtk, so five samples produce six results.

```sh
export DOTNET_ROOT="$PWD/.tools/dotnet"
python3 eng/test-samples.py --platform linux --framework-dependent
```

For a VM without a desktop session, install Xvfb and Openbox, then run:

```sh
xvfb-run -a sh -c 'openbox >/tmp/nativechannels-openbox.log 2>&1 & python3 eng/test-samples.py --platform linux --framework-dependent --virtual-audio'
```

`--framework-dependent` saves disk space and requires the matching .NET runtime.
`--virtual-audio` creates a temporary PulseAudio null sink, routes the test process
to it, and unloads it afterward without changing the default sink. This exercises
buffer/progress behavior but does not establish audible playback. Omit it to use
the real output device. The runner does not change the system volume.

## Android

Use an unlocked API 31+ emulator or device, Android SDK platform tools (`adb`), the
pinned .NET Android workload, the SDK/build tools required by that workload, and the
JDK required by the repository's Gradle version. Boot the emulator first.

```sh
adb devices -l
python3 eng/test-samples.py --platform android --device emulator-5554
```

Use `--adb /path/to/adb` if it is not on PATH. The runner installs the sample APKs,
launches their verification mode, reads logs filtered to the newly launched process,
and force-stops each sample afterward. It does not clear device logs. Specify a serial
when multiple devices are connected. Android setup and emulator creation are separate
from the test runner.

## Apple platforms

On your Mac, install the pinned SDK/workloads, compatible Xcode, and its simulator
runtimes. The build uses the repository's existing Swift/XCFramework scripts.

```sh
python3 eng/test-samples.py --platform macos
xcrun simctl list devices booted
python3 eng/test-samples.py --platform ios --device <booted-simulator-UDID>
```

Architecture defaults to the machine architecture; `--arch x64` or `--arch arm64`
overrides it. The iOS runner targets simulators; physical iOS devices and signing are
not automated by this script. Apple execution must be verified on the Mac.

## Browser

Install Node.js, the pinned SDK's `wasm-tools` workload, and Playwright:

```sh
npm ci --prefix tests/NativeChannels.Browser.Tests
npx --prefix tests/NativeChannels.Browser.Tests playwright install chromium
python3 eng/test-samples.py --platform browser
```

The runner serves each published WebAssembly app on a loopback address, launches
Chromium, and clicks a verification button to grant the audio user gesture. Browser
volume tests exercise the page-owned audio element; browsers do not expose OS volume.
Set `BROWSER_CHROMIUM_CHANNEL=chrome` to use an installed Chrome channel instead.

## Repeat runs and coverage

Use `--build-only` to prepare artifacts without launching them. It prints a manifest:

```sh
python3 eng/test-samples.py --platform windows --manifest <build/manifest.json>
python3 eng/test-samples.py --platform windows --manifest <build/manifest.json> --sample PdfViewer
```

Manifest reuse tests those existing binaries; it does **not** rebuild changed source.
The report records build source and current source separately. Use a fresh build for
final acceptance. `--reuse-external-cache <NuGet-cache>` can hardlink external packages
to save disk space; framework and plugin packages always use a fresh scoped cache.
`--timeout 120` controls the per-sample process deadline.

| Sample | Automated checks |
| --- | --- |
| Currency | Native symbols, normalization, invalid codes, concurrency, cancellation, cleanup |
| AudioStreamer | PCM progress, bounded buffering, pause/resume, cancellation, drain, restart; desktop Windows/Linux additionally exercise host recreation and controls |
| VolumeMonitor | Initial levels, independent listeners, cancellation, restart; browser page-volume change |
| PdfViewer | Bundled three-page PDF, two native instances, page/zoom state, detach/reconnect and reopen |
| Interop | Typed calls, codecs, callbacks, UI dispatch, errors, concurrent calls, cancellation, streams, native-view lifetime and reconnect stress; off-thread rejection on threaded targets |

These are integration checks against real native adapters. They do not replace visual
PDF inspection, audible audio checks, hardware volume-button tests, or device-specific
accessibility and lifecycle testing. PDF state assertions do not prove pixel fidelity.
The single-threaded browser runtime cannot exercise background-thread rejection; its
result explicitly identifies that exclusion.

Runner contract tests can execute without a device:

```sh
python3 -m unittest discover -s eng/tests -p test_local_samples.py
```
