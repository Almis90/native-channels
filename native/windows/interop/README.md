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

## Rust runtime dependency

The Rust `x86_64-pc-windows-msvc` DLL imports `VCRUNTIME140.dll`. A machine with
the .NET runtime alone may not have this Visual C++ runtime dependency. On the
Windows 10 test VM, C++ passed but Rust initially failed with
`DllNotFoundException` / `0x8007007E` when loading the existing Rust DLL because
its dependency was missing.

Deploy the appropriate x64 Visual C++ Redistributable with the application, or
use a supported application-local runtime deployment. The test used a
Microsoft-signed `vcruntime140.dll` version `14.51.36247.0` copied beside the test
app, with that directory added to the test process's `PATH`. It did not install
the runtime system-wide or change the VM's persistent `PATH`. Simply copying
the Rust DLL is therefore insufficient on a clean machine. The C++ probe uses
`/MT` and passed without this additional DLL.

See Microsoft's [deployment guidance](https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method?view=msvc-170).
The application-local copy above records this test setup; it is not a production
installer or a recommendation to copy arbitrary DLLs from another computer.

## Windows execution results — 2026-10-16

| Host | Result | Setup |
| --- | --- | --- |
| Windows 11 Home x64, build 26200 | C++ and Rust passed | Existing host runtime dependencies; harness built with .NET SDK 10.0.401 |
| Windows 10 Enterprise LTSC 2019 Evaluation x64 VM, build 17763.107 | C++ and Rust passed after supplying the Rust dependency | .NET runtime 10.0.12; application-local `vcruntime140.dll` 14.51.36247.0 |

Each implementation passed native identity and Win32 process-ID checks, signed
64-bit limits and overflow errors, missing-method/argument errors, empty/binary/
UTF-8 payloads, 32 concurrent calls, pre-cancelled requests, recovery after errors,
and ten load/dispose cycles. Both modules also passed coexistence in one transport.
The VM was shut down after testing. No MSIX installation or UI test was performed.

These were **runtime tests of CI-built native DLLs**, not local C++/Rust compiler
tests. The C# harness was built locally and the same harness was copied to the VM.
Both hosts used the DLLs from [PR CI run 38896283678](https://github.com/Almis90/native-channels/actions/runs/38896283678),
artifact `12043698548` (`windows-cpp-rust-evidence`). The tested branch commit was
`0dc8988c98447430d47f699b871019668e4f783a`; the CI DLLs were built at PR merge commit
`9725359a896d6d090fa5a3280a259f858fc67e87`. Their source trees were verified identical.
CI used Rust 1.90.0 and MSVC 14.44.35207.

SHA-256 values checked before execution:

| Artifact | SHA-256 |
| --- | --- |
| CI artifact ZIP | `0b969af535437ef326248546771ce64623e81bc9c75ee211a827a6ed4b90530a` |
| C++ DLL | `62e842084391ae797e7b610718eefdf718d61c0dfec114a37405c4eea9d05c14` |
| Rust DLL | `853236880b8f60c85fb771efebb48e3d82a107f802f1d4c071e623dea313cc8c` |

The Windows 10 application-local runtime copy was recorded with SHA-256
`d1f4225df2cd877dbf130d5668a021dce3f94118455ff5ec952061c30afc9ce7`.
Local evidence is retained under `artifacts/windows-interop-local-20261016/`:
`local-verification.json`, `local-verification.log`,
`windows10-verification.json` and `windows10-verification.log`. These ignored local
files are not included in the repository; the table and hashes above preserve
the verification record after the temporary CI artifact expires.

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
views, NativeAOT, ARM64 and MSIX deployment are not
established by this experiment. Those require additional ABI/lifetime design and
separate tests. The existing C# Windows adapters remain available.

References: [.NET calling conventions](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/calling-conventions)
and [Rust FFI](https://doc.rust-lang.org/nomicon/ffi.html).
