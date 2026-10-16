# Roadmap

NativeChannels begins with the existing channel implementation and MAUI integration for Android,
iOS, Mac Catalyst, and Windows. Version 0.2 adds complete Avalonia integration, including
browser/WebAssembly; version 0.3 adds Uno integration. Each host owns its startup, UI dispatch,
shutdown, and view hosting while reusing the shared channel contracts and plugin packages.

| Milestone | Outcome | Acceptance |
| --- | --- | --- |
| **0.1 — NativeChannels preview** | A reliable four-platform foundation | Verify packages in separate consumers; finish the remaining platform matrix and document limitations |
| **0.2 — Complete Avalonia integration** | A usable Avalonia host on Android, iOS, Windows, macOS, Linux, and browser/WebAssembly | Host packages, all channel contracts, platform and browser view hosting, reference-plugin integration, fresh-package runtime checks, and documentation for all six targets |
| **0.3 — Uno integration** | Reuse the shared framework and plugins in Uno applications | Uno registration, lifecycle, dispatch, and view integration; package-only samples and runtime acceptance across Android, iOS, Windows, macOS, Linux, and browser/WebAssembly |
| **1.0 — Stable API and distribution** | Public contracts ready for long-term use | Compatibility checks, supported-platform acceptance, lifecycle and performance evidence, and reviewed distribution |

## Initial preview

Implemented features include method calls, binary/value messages, independent event subscriptions,
and native views with instance channels. The four reference plugins demonstrate these contracts.

Remaining acceptance work includes fresh package-only release validation, Windows ARM64
execution, broader physical-device and audio-route checks, and long-running native lifecycle tests.
The [verification record](verification.md) distinguishes implemented, compiled, and executed behavior.

## Avalonia integration in 0.2

The milestone is complete only when an application can install the host and plugin packages and
use the documented integration on all six targets. The Android Currency proof is the first slice;
merging it does not complete 0.2. The host package, desktop and iOS integrations, and browser support
all belong to this milestone.

### Current progress

`NativeChannels.Core` remains independent of application frameworks. Shared module configuration
and DI registration now live in the platform package; the MAUI host adds startup and native view
integration. Currency uses this shared registration without depending on MAUI. Framework hosts
supply dispatchers, startup/shutdown integration, and native view containers.

The [Avalonia Android Currency sample](../samples/Currency.Avalonia.Android/README.md) and its dedicated
verification workflow implement the first proof: one Currency package consumed by both MAUI and
Avalonia. The workflow checks matching archive hashes, separate fresh caches, no MAUI dependencies
in Avalonia, and matching runtime results. See [verification](verification.md) for executed evidence.
Only this Android Currency slice has executed Avalonia evidence. The remaining items below are
planned acceptance work, not current support claims.

### Required targets

The target set includes both the native and browser targets in
[Avalonia's platform list](https://docs.avaloniaui.net/docs/supported-platforms). Pin supported OS,
architecture, Avalonia, .NET, and browser versions in the verification matrix before release.

| Target | Work required for 0.2 |
| --- | --- |
| Android | General host registration, activity lifecycle, dispatch, all channel contracts, and native view integration beyond the Currency proof |
| iOS | Avalonia application lifecycle and UIKit view integration using the iOS backend; simulator and device evidence recorded separately |
| Windows | Avalonia window ownership, UI dispatch, and a native view adapter; verify supported Windows versions and architectures |
| macOS | Desktop macOS transport, native assets, AppKit view hosting, and package consumers; Mac Catalyst evidence does not cover this target |
| Linux | Native transport and assets, desktop lifecycle, and view hosting for the explicitly supported display backends and architectures |
| Browser/WebAssembly | Browser transport and JavaScript adapters, web assets, DOM view hosting, and tests of the declared supported browser engines |

The browser backend must preserve channel semantics while implementing features through web APIs.
Document and test browser permissions, user-gesture requirements, and unsupported capabilities;
native OS libraries and controls need platform-specific web equivalents. A browser target is still
required for 0.2, even where an OS-specific plugin capability cannot be provided by the browser.

### Completion criteria

- Provide a reusable Avalonia host package, with documented registration, startup, UI dispatch,
  cancellation, shutdown, and per-window or per-activity ownership. Avalonia consumers must not
  acquire a MAUI dependency.
- Verify method calls, value and binary messages, independent event subscriptions, structured
  errors, timeouts, cancellation, concurrency, and disposal on every target.
- Implement native or browser view hosting with instance channels, UI-thread ownership, creation,
  detach/reconnect, and cleanup checks. Record platform-specific focus, sizing, and embedding limits.
- Integrate Currency, PdfViewer, AudioStreamer, and VolumeMonitor through shared plugin contracts.
  Run supported features in Avalonia; document and test explicit capability failures where the OS
  or browser cannot provide a feature. Keep plugin logic independent of the UI host.
- Consume the same shared package archives in MAUI and Avalonia wherever their platforms overlap.
  Add macOS, Linux, and browser assets and package-only consumers; no source project references or
  warmed development caches may substitute for release-package acceptance.
- Add CI and runtime evidence for all six targets, covering the declared OS, architecture, display
  backend, and browser matrix. Keep compile-only, emulator/simulator, and device results distinct.
  Preserve regression coverage for existing MAUI targets.
- Publish complete setup, plugin-authoring, lifecycle, platform capability, and troubleshooting
  documentation. Audit and test the exact packages intended for the 0.2 NuGet release using the
  [release checklist](release-checklist.md).

### Implementation sequence

1. Finish the shared registration foundation and reusable Avalonia host/lifecycle contracts.
2. Extend Android and add Windows and iOS, exercising all channel types and view lifetimes.
3. Add macOS and Linux transports, assets, and host/view integration.
4. Add the browser/WebAssembly transport, JavaScript plugin adapters, and DOM view integration.
5. Complete the reference-plugin matrix, CI, documentation, and combined-package release acceptance.

The steps can land as separate pull requests. All are part of 0.2; none requires waiting for Uno.

## Uno integration in 0.3

Add a reusable Uno host that consumes the shared registration, transports, and plugin contracts
without depending on the MAUI or Avalonia host packages. Plan for Android, iOS, Windows, macOS,
Linux, and browser/WebAssembly, matching [Uno's platform list](https://platform.uno/docs/articles/getting-started/requirements.html).

Define Uno startup/shutdown ownership, UI dispatch, and native or browser view integration for each
supported renderer and platform. Verify all channel contracts and reference-plugin capabilities in
fresh package-only consumers, including reuse of identical shared packages across hosts where
targets overlap. Complete the support matrix, regression CI, documentation, and release-package
acceptance before calling 0.3 complete. Avalonia execution does not establish Uno runtime support.

## Stable release

Use the [release checklist](release-checklist.md). Package publication and release creation remain
separate actions; completing a milestone does not publish to NuGet.org.
