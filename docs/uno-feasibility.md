# Uno 0.3 feasibility

Status: **investigation started; six-platform feasibility is not established**.
The first gate identified desktop transport and macOS native-view blockers.
Step 2 adds runtime selection to the portable transport and a plain-.NET macOS
bridge. Step 3 introduces an experimental Uno host; native-view feasibility remains open.

## Pinned baseline

- .NET SDK: 10.0.401, matching the repository.
- Uno.Sdk and Uno.Templates: 6.7.30, stable NuGet packages.
- Uno runtime resolved by that SDK: 6.7.135.
- Runtime source commit recorded in the package: `da217df2312e9e39676880f9cb4b8d519cc297e0`.
- Initial backend: Skia; Windows App SDK is a separate, untested configuration.

The isolated [probe project](../samples/Uno/Feasibility/README.md) has its own SDK
and dependency configuration. It does not change the existing MAUI or Avalonia
builds or bump the framework release version.

## Initial step 1 results on October 19, 2026

The blank Uno desktop application and the package-consuming probe both built in
Release on Windows with zero warnings and errors. The probe consumed freshly
packed `NativeChannels.Core` and `NativeChannels` 0.2.0-preview.1 packages from
this checkout, using a separate cache and no library project references.
The final reproduction script completed using that existing cache. Its additional
fresh-cache run exhausted local disk space; that run is not counted as a pass.
Evidence is retained under `artifacts/uno-feasibility/`, including dependency JSON,
view results, resolved assets and package hashes in each completed `runs/` directory.

| Target | Method-call feasibility | Native-view feasibility | Evidence level |
| --- | --- | --- | --- |
| Windows, Skia desktop | Blocked before calls: portable transport rejects Windows | Hidden button attachment/recreation probe returned false for both parent checks; cause unresolved | Built and executed |
| macOS, Skia desktop | Existing Apple binding is platform-targeted; desktop compatibility needs work | Released native wrapper and hosting extension are internal | Released binary metadata and matching source inspected; not executed on macOS |
| Linux, X11 desktop | Not executed; existing portable transport is Linux-specific | Public X11 wrapper exists; toolkit/loop integration untested | Source inspected |
| Android, Skia | Not executed | Android native-view path exists in released source; not tested here | Template head only |
| iOS, Skia | Not executed | UIKit native-view path exists in released source; not tested here | Template head only |
| Browser, Skia | Not executed; asset selection/loading must be proved | Public DOM wrapper exists; not tested here | Template head only |

### Desktop package selection

The restored Uno `net10.0-desktop` target selects
`NativeChannels/0.2.0-preview.1/lib/net10.0/NativeChannels.dll`. Our current
`net10.0` assembly at the step 1 baseline was compiled with `LINUX`. On Windows, constructing its transport
throws `PlatformNotSupportedException: The Linux transport requires Linux.`
No native method call ran. Successful restore/build is therefore insufficient.

Uno's desktop runtime uses plain .NET assets for its AppKit shell, while our Apple
transport relies on `Foundation`, `ObjCRuntime` and the `net10.0-macos` binding.
Do not assume that the current Apple package can be consumed unchanged. The next
transport phase must establish platform selection and a compatible macOS bridge,
potentially through a plain-.NET C ABI shim over the existing native implementation.
Step 2 implements that approach; see the desktop transport results below.

### macOS native views

The dependency probe reads the actual restored `Uno.UI.Runtime.Skia.MacOS.dll`
with `System.Reflection.Metadata`. Both `MacOSNativeElement` and
`MacOSNativeElementHostingExtension` have `NotPublic` visibility.
The matching [released source](https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI.Runtime.Skia.MacOS/Native/MacOSNativeElementHostingExtension.cs)
accepts only the internal wrapper in `IsNativeElement` and routes ownership,
attachment and layout through Uno's native implementation.
Uno's [embedding documentation](https://platform.uno/docs/articles/features/using-skia-hosting-native-controls.html)
also lists macOS embedding as unsupported.

This establishes a missing public integration path in the selected release, not
that embedding is fundamentally impossible. A supported Uno extension/upstream
change must be investigated before promising AppKit/PDFKit embedding. Reflection
into internal types is not an accepted production design. A rendered PDF image
or a separate native window would not satisfy the planned embedded-view contract.

### Windows native-view probe

The automated hidden launch created native button windows but the parent checks
after assignment to Uno content returned false, including after recreating the
button. This result is unresolved: it does not establish that Uno's documented
Win32 embedding API is unsupported. Investigate host activation/layout and native
wrapper behavior, then verify a visible interactive instance, input and focus.
The probe explicitly records these latter checks as unverified.

## Step 2: portable desktop transport

The portable `net10.0` assembly now selects the Windows, Linux, or macOS transport
at runtime. `NativeChannelsDesktopOnly=true` builds this assembly; the existing
`NativeChannelsLinuxOnly=true` switch remains compatible. Windows and Linux reuse
their existing adapters. Specialized Apple, Android, browser, and Windows targets
retain their existing transport paths.

macOS uses a small exported C ABI around the existing Swift `NativeBridge`.
It passes channel envelopes unchanged and does not reference .NET Apple bindings
or Uno UI types. Native module classes retain their existing names. Adapter dylibs
are loaded once per process because Objective-C class registration is global.
The bridge normalizes UUID casing when correlating native-to-managed replies.

Local packages use version `0.3.0-preview.3`; this is a probe version, not a release
version bump. Build both macOS architectures with `eng/build-apple-desktop.py` and
pack with `NativeChannelsIncludeAppleDesktopAssets=true` to include
`runtimes/osx-arm64/native/libNativeChannelsNative.dylib` and its `osx-x64` sibling.
The test-only `NativeChannels.Desktop.ProbeAssets` package supplies Currency and
sample adapter dylibs. Production plugin packaging and release CI are still later
work; this fixture is not a new plugin product or a published package.

Executed checks:

- Windows: the package-only Uno consumer calls Win32 `GetDoubleClickTime` through
  the portable transport. Nine desktop transport tests pass, including factory
  selection, callbacks, messages, streams, cancellation, and session cleanup.
- macOS ARM64: the package-only Uno consumer loads the Swift Currency adapter and
  returns `€`. Three successive native sample sessions pass method callbacks,
  Int64 envelopes, native errors, missing methods, binary callbacks, finite events,
  request cancellation, reuse after cancellation, and disposal with pending work.
- macOS x64: native libraries cross-compile; execution has not been verified.
- Linux x64 (Ubuntu VM): the portable runtime-selection test passes, including
  a method round trip and module disposal. A package-only Uno Linux run remains pending.
- Existing managed regression suites: 135 tests pass (Core, Windows, Currency,
  and Avalonia).

Reproduce the macOS package gate with `python3 eng/verify-uno-apple-desktop.py`
with the pinned SDK on `PATH`. It builds both native architectures, checks RID
assets in the package, restores a package-only Uno consumer into a fresh cache,
and retains JSON results, contract logs, and package SHA-256 hashes. A failed
contract makes this gate fail. These checks do not validate UI dispatch or native
views; the Windows control probe and macOS public hosting API remain unresolved.
The fresh-cache macOS run passed with evidence under
`artifacts/uno-feasibility/runs/b8b047d602884cc280d961b7a064a49b/`.

## Step 3: optional Uno host

`NativeChannels.Uno` now provides application registration, per-window service
providers and sessions, a window-bound dispatcher, and deterministic asynchronous
shutdown. Ownership logic is shared with Avalonia under `src/Shared.Hosting`;
the Uno layer depends on Core, Uno.WinUI and DI, without navigation packages.
See [Uno host setup and limitations](uno-host.md).

The package-only Windows Uno host probe runs inside the real UI event loop. It
checks independent window sessions, asynchronous UI continuations, cancellation
before queued work starts, exception propagation, view reconnect and disposal,
stale channel invalidation, and shutdown with pending work. The view fixture uses
a managed element to test lifecycle; it is not evidence of native embedding.
Avalonia's existing 12 host tests also pass against the extracted ownership code.
The final Windows host run passed using a reused dependency cache; evidence is in
`artifacts/uno-feasibility/runs/1293f58341c343e38d854a3831619dd4/`.

The proposed `UnoChannelView` is experimental. macOS explicitly rejects native
attachment because the pinned Uno release lacks the required public API. Windows
native parenting remains unresolved. Step 3's native-view acceptance is therefore
still open, and no other platform's UI host has been executed for this step.

## Next gate

1. Resolve the Windows control probe and prove a packaged method call on a
   compatible Windows configuration.
2. Extend desktop package acceptance into production plugin packaging and release
   CI, retaining the plain-.NET macOS bridge outside the UI host package.
3. Establish a supported macOS native-element extension path against this pinned
   release, or identify the upstream change needed. Do not silently reduce the
   six-target native-view acceptance requirement.
4. Complete Android, iOS, Linux and browser method/control probes with fresh
   package feeds and real runtimes. Record each backend, architecture, package
   hash and execution result separately.

Keep the full host, PDF integration and five-sample expansion gated on these
findings. No runtime support is inferred from template generation or source inspection.
