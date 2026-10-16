# C++ and Rust Windows channel examples

Both DLLs implement the same small C ABI. `NativeDllModule.cs` connects that ABI to
the existing `IWindowsChannelModule` registration. Application calls still use
`MethodChannel` and `BasicMessageChannel`; addition, byte transformation and the
Win32 `GetCurrentProcessId` call execute inside the chosen C++ or Rust DLL.

This is an interoperability experiment, not a new transport or a complete native
channel SDK. A small C# adapter remains responsible for channel registration,
serialization, native error translation and DLL lifetime. There is no change to
the public framework API or existing Windows plugins.

## Build and run

On Windows x64, install PowerShell 7, the .NET SDK pinned in `global.json`, Visual
Studio C++ x64 build tools with a Windows SDK, and Rust via rustup. Then run:

```powershell
rustup toolchain install 1.90.0 --profile minimal --component rustfmt --target x86_64-pc-windows-msvc
./eng/verify-windows-interop.ps1
```

No MAUI workload or third-party Rust crates are required. The C++ example uses
MSVC's static CRT; Rust uses its normal MSVC target runtime. Output is written to
`artifacts/windows-interop`, including DLL hashes and the tested source commit.
The **Windows / C++ and Rust interop** workflow runs the same verifier on Windows.

The executable links the shipping Windows transport source, just like the existing
Windows transport tests. It tests both DLLs through serialized channels, including
native implementation identity, native Win32 calls, signed 64-bit boundaries,
overflow errors, empty/binary/UTF-8 payloads, concurrent callers, pre-cancelled
requests, repeated transport disposal and both modules coexisting.

## Use in a Windows application

Include `NativeDllModule.cs` in the Windows application, enable unsafe blocks, and
deploy the matching architecture's DLL alongside the app. Register a fresh module:

```csharp
options.Modules.Add(new("", "")
{
    WindowsFactory = () => new NativeChannels.InteropSample.NativeDllModule(
        Path.Combine(AppContext.BaseDirectory, "native_channels_rust_probe.dll"),
        "native.math"),
});
// For C++, use native_channels_cpp_probe.dll with the same adapter.
```

Then call the channel using its built-in dictionary serialization:

```csharp
await channels.GetMethodChannel("native.math").InvokeAsync<long>(
    "add", new Dictionary<string, object?> { ["a"] = 20L, ["b"] = 22L });
```
Use a trusted absolute DLL path. Missing or incompatible exports fail at module
creation; a factory failure is reported by the transport and its resources are cleaned up.

## ABI and limits

`native_channels_probe.h` defines the shared ABI: fixed-width integers, C exports,
cdecl calling convention, synchronous operations and integer status codes.
The caller owns all buffers. Neither DLL retains pointers, allocates returned
memory, throws exceptions across the boundary or calls back after returning.
The adapter pins byte arrays only for the native call and serializes calls against
disposal so it cannot unload a DLL while executing its code.

Cancellation is checked before entry; it cannot interrupt these synchronous DLL
functions. Native callbacks, asynchronous native work, events, embedded native
views, NativeAOT, ARM64, MSIX deployment and Windows 10 runtime execution are not
established by this experiment. Those require additional ABI/lifetime design and
separate tests. The existing C# Windows adapters remain available.

References: [.NET calling conventions](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/calling-conventions)
and [Rust FFI](https://doc.rust-lang.org/nomicon/ffi.html).
