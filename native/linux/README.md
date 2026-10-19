# Linux native modules

Currency and AudioStreamer implement their Linux channel handlers in **C++17**.
Currency links system ICU; AudioStreamer links ALSA and owns its PCM device, bounded
queue, worker thread and resource lease. Neither plugin has a C# Linux implementation.
The Avalonia UI and shared managed facade remain .NET code.

`LinuxNativeModule` is the reusable host bridge. It loads a packaged `.so` and forwards
channel requests through the four exports in [native_channels.h](include/native_channels.h):
`nc_create`, `nc_request`, `nc_cancel`, and `nc_destroy`. There are no method-specific
C# bindings or ICU/ALSA P/Invokes. Any language capable of exporting this C ABI can
implement a module; C++ is the implementation demonstrated here, not a language requirement.

The host passes the channel, method, StandardMessageCodec arguments and current locale.
The native module replies with a value, structured error or not-implemented result.
Binary channels carry byte arrays through the same boundary. The small C++ codec in
[channel.hpp](include/channel.hpp) supports the reference modules' scalar values, byte
arrays, lists and string-keyed maps; typed numeric arrays and arbitrary map keys are
explicitly unsupported. This is not yet a complete Linux native SDK for all channel types.

## Lifetime contract

- Input buffers are borrowed only during the request. Copy anything needed asynchronously.
- Reply buffers are borrowed during the callback; the managed bridge copies them immediately.
- A request ID receives exactly one reply. Audio's `open` deliberately stays pending as a lease.
- Cancellation closes that lease; stop, device failure and destruction also complete it.
- Destroy must stop/join workers and finish pending requests before returning. The host then
  releases callback delegates and unloads the library. Native code must never call back later.
- Exceptions are translated to structured errors inside C++; none crosses the C ABI.

## Build and test locally

On Ubuntu 24.04 x64:

```sh
sudo apt-get install g++ pkg-config libicu-dev libasound2-dev
bash eng/build-linux-native.sh currency
bash eng/build-linux-native.sh audio
bash eng/test-linux-native.sh
bash eng/dotnet.sh test tests/NativeChannels.Linux.Tests -c Release
bash eng/dotnet.sh test tests/Plugin.AudioStreamer.Linux.Tests -c Release
```

The C++ audio tests use a fake native device to check partial writes, bounds, pause,
failure/restart, cancellation and destruction, with AddressSanitizer and UndefinedBehaviorSanitizer.
The .NET integration tests cross the actual packaged ABI and use system ICU and ALSA.
Audio hardware tests require a working pause-capable output; they use silent PCM.

Plugin builds with `NativeChannelsLinuxOnly=true` compile the libraries and package them
under `runtimes/linux-x64/native/`. Consumers need only the packages and system runtime
libraries, not a C++ compiler. These local development packages target Ubuntu 24.04 x64;
ICU's SONAME is determined by the build machine (ICU 74 here). Rebuild for a different ICU
ABI/distribution. Other architectures, trimming and NativeAOT need separate verification.

PdfViewer is still planned: separate **Qt + Qt PDF** and **GTK + Poppler** Linux examples,
sharing the managed channel contract. Neither PDF backend is implemented by this work.
