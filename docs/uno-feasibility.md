# Uno 0.3 feasibility

Status: **investigation started; six-platform feasibility is not established**.
The first gate has identified desktop transport and macOS native-view blockers.
No Uno host package or production transport changes have been introduced.

## Pinned baseline

- .NET SDK: 10.0.401, matching the repository.
- Uno.Sdk and Uno.Templates: 6.7.30, stable NuGet packages.
- Uno runtime resolved by that SDK: 6.7.135.
- Runtime source commit recorded in the package: `da217df2312e9e39676880f9cb4b8d519cc297e0`.
- Initial backend: Skia; Windows App SDK is a separate, untested configuration.

The isolated [probe project](../samples/Uno/Feasibility/README.md) has its own SDK
and dependency configuration. It does not change the existing MAUI or Avalonia
builds or bump the framework release version.

## Results on October 19, 2026

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
`net10.0` assembly is compiled with `LINUX`. On Windows, constructing its transport
throws `PlatformNotSupportedException: The Linux transport requires Linux.`
No native method call ran. Successful restore/build is therefore insufficient.

Uno's desktop runtime uses plain .NET assets for its AppKit shell, while our Apple
transport relies on `Foundation`, `ObjCRuntime` and the `net10.0-macos` binding.
Do not assume that the current Apple package can be consumed unchanged. The next
transport phase must establish platform selection and a compatible macOS bridge,
potentially through a plain-.NET C ABI shim over the existing native implementation.
This is a proposed approach, not an implemented solution.

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

## Next gate

1. Resolve the Windows control probe and prove a packaged method call on a
   compatible Windows configuration.
2. Implement or prototype desktop transport selection and macOS bridge loading
   outside the UI host package (the transport work from plan step 2).
3. Establish a supported macOS native-element extension path against this pinned
   release, or identify the upstream change needed. Do not silently reduce the
   six-target native-view acceptance requirement.
4. Complete Android, iOS, Linux and browser method/control probes with fresh
   package feeds and real runtimes. Record each backend, architecture, package
   hash and execution result separately.

Keep the full host, PDF integration and five-sample expansion gated on these
findings. No runtime support is inferred from template generation or source inspection.
