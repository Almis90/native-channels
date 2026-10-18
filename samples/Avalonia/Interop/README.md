# Avalonia interoperability sample

This ports `samples/Maui/ChannelExplorer` to Android, iOS, Windows, macOS,
Linux and browser. It is the framework-contract sample alongside the four
Currency, AudioStreamer, VolumeMonitor and PdfViewer reference samples.

The app links the original `NativeVerification.cs`, models and JSON serializer
context. Its Avalonia view checks use the reusable `AvaloniaChannelView` host.
Framework libraries are consumed as NuGet packages; sample adapters belong to
the app, as in the MAUI sample.

| Target | Native adapter and embedded button |
|---|---|
| Android | Existing Kotlin sample module and Android button factory |
| iOS | Existing Swift sample module and UIKit button factory |
| Windows x64 | Existing Windows sample module, Win32 child buttons |
| macOS | Existing Swift sample module, new AppKit button factory |
| Linux x64 | C++ sample module, GTK 3 buttons under X11/XWayland |
| Browser | JavaScript module and DOM buttons |

Linux calls GTK in C++. The C# transport only marshals generic channel messages
through the C ABI. Its optional callback extension lets a native module invoke
managed handlers and receive their replies asynchronously. Linux and browser
basic-message adapters forward opaque bytes to the managed peer and return its
reply, preserving every selected codec's payload. They do not parse the full
StandardMessageCodec format themselves.

## Build

Install the pinned SDK with `bash eng/bootstrap.sh` and the target's SDK/workload
prerequisites, then run:

```sh
bash eng/build-interop-avalonia.sh linux
# Other targets: android, ios, windows, macos, browser
```

Linux needs a C++17 compiler, pkg-config, GTK 3 and X11 development headers
(`build-essential pkg-config libgtk-3-dev libx11-dev` on Ubuntu). Running its
embedded buttons requires an X11 session or XWayland. Native macOS and iOS builds
require macOS/Xcode. Android builds require the Android SDK and Java toolchain.
Browser builds require the .NET WebAssembly workload and Node.js.

The script builds the framework packages and app-owned adapters, restores from
`artifacts/interop-avalonia/<target>/packages`, builds the head and checks the
consumer's package hashes. It uses an isolated package cache and refuses to
overwrite an existing target output directory. Preserve or remove that generated
directory before a fresh rebuild. It does not launch the app or run workflows.

## Run later

Launch the built head on its platform and press **Run checks**. Checks cover typed
calls/DTOs, bytes, null, errors, UI dispatch, concurrent requests, cancellation,
timeouts, handle/session disposal, bidirectional JSON/string/binary/standard
messages, finite/null/error events and independent stream cleanup. Two native
buttons cover creation arguments, independent instances, callbacks with managed
acknowledgements, reconnect, unload/reload, stale channels and 32 reconnect cycles.
Manual taps and **Recreate first native view** also demonstrate the view contract.
The suite resets both views before starting; avoid manual taps while it runs.

The result appears in the app and on stdout with prefix
`NATIVECHANNELS_AVALONIA_INTEROP`. Non-browser heads also write it to the path in
`NATIVECHANNELS_SAMPLE_RESULT` when explicitly set. A successful build is not a
runtime result: all six heads still need the platform execution pass.
