# Roadmap

NativeChannels begins with the existing channel implementation and MAUI integration for Android,
iOS, Mac Catalyst, and Windows. The next step is to make native integrations reusable across UI
frameworks, while keeping platform lifecycle and native view hosting explicit.

| Milestone | Outcome | Acceptance |
| --- | --- | --- |
| **0.1 — NativeChannels preview** | A reliable four-platform foundation | Verify packages in separate consumers; finish the remaining platform matrix and document limitations |
| **0.2 — Complete Avalonia integration** | Avalonia on Android, iOS, Windows, macOS, Linux, and browser/WebAssembly | Reusable host packages, all channel contracts, native/DOM views, reference plugins, package-only consumers, runtime CI, documentation, and NuGet release acceptance across all six targets |
| **0.3 — Complete Uno integration** | Uno on Android, iOS, Windows, macOS, Linux, and browser/WebAssembly | Apply the same six-target completion criteria to the Uno host, reusing the shared contracts and adapters where compatible |
| **1.0 — Stable API and distribution** | Public contracts ready for long-term use | Compatibility checks, supported-platform acceptance, lifecycle and performance evidence, and reviewed distribution |

These are planned milestones, not a support matrix. MAUI remains the general application host;
[verification](verification.md) records compiled and executed configurations, including the initial
Avalonia Android Currency proof. Complete Avalonia, Uno, and browser/WebAssembly integration
remain release goals. The [Avalonia sample matrix](../samples/Avalonia/README.md) now records source implementations for all four reference samples plus the [interoperability sample](../samples/Avalonia/Interop/README.md) on all six targets; new implementations still need runtime verification. Progress on a feature branch does not establish release acceptance.

## Initial preview

Implemented features include method calls, binary/value messages, independent event subscriptions,
and native views with instance channels. The four reference plugins demonstrate these contracts.

The framework preview `0.1.0-preview.2` is [published on NuGet.org](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.2),
following exact-source distribution validation and fresh public-feed consumer checks.
Remaining coverage includes Windows ARM64 execution, broader physical-device and audio-route checks,
and long-running native lifecycle tests. Each future release still requires fresh package-only validation.
The [verification record](verification.md) distinguishes implemented, compiled, and executed behavior.

## Shared hosting

`NativeChannels.Core` remains independent of application frameworks. Shared module configuration
and DI registration now live in the platform package; the MAUI host adds startup and native view
integration. Currency uses this shared registration without depending on MAUI. Framework hosts
supply dispatchers, startup/shutdown integration, and native view containers.

The [Avalonia Android Currency sample](../samples/Avalonia/Currency/Currency.Android/README.md) and its dedicated
verification workflow implement the first proof: one Currency package consumed by both MAUI and
Avalonia. The workflow checks matching archive hashes, separate fresh caches, no MAUI dependencies
in Avalonia, and matching runtime results. See [verification](verification.md) for executed evidence.
The [Avalonia iOS sample](../samples/Avalonia/Currency/Currency.iOS/README.md) extends the same package and
method-call scenarios to Apple's simulator with its own required verification job. Its execution
status is recorded separately in [verification](verification.md).
The [Avalonia native macOS sample](../samples/Avalonia/Currency/Currency.Desktop/README.md) uses `net10.0-macos`,
AppKit and a macOS Swift XCFramework through the same Apple channel contracts. Its separate workflow
builds x64/ARM64 and executes ARM64; it does not use the MAUI Mac Catalyst host.
Extend these proofs to messages and event streams before expanding native view hosting. The [Avalonia host package](avalonia-host.md) now supplies portable registration, owned sessions
and UI dispatch. `AvaloniaChannelView` now supplies native/DOM view attachment ownership; the broader lifecycle/contract verification matrix remains outstanding.
The Android Currency proof is the first part of 0.2: reuse one plugin through packages in MAUI and
Avalonia Android applications. It establishes a starting point for shared registration and method
calls only. It does not complete Avalonia integration, prove the other channel contracts, or verify
any other target. Shared hosting is part of the complete 0.2 milestone, not a separate release goal.

## Avalonia integration

The [Linux Currency proof](currency-avalonia-linux.md) extends the Desktop head with a Linux
transport and a system-ICU adapter, package-only X11 execution, and a dedicated runtime workflow.
It is a method-call sample, not completion of Linux hosting or the broader milestone.

The [Browser Currency proof](currency-avalonia-browser.md) adds a packaged ES-module bridge and
Intl adapter, with package-only WebAssembly execution and a Chromium/Firefox workflow. It also
exercises binary/value messages, managed callbacks and transport cleanup. Browser event helpers,
Web Audio, page-volume events and PDF.js DOM views are now implemented with runtime verification pending.

The [AudioStreamer Windows sample](audio-avalonia-windows.md) starts the next reference-plugin
integration: framework-neutral registration and dependencies, a package-only Avalonia consumer,
and the existing native PCM adapter. Its local verification covers playback, backpressure,
DI ownership and window shutdown. The [Linux head](audio-avalonia-linux.md) shares that UI
and adds an ALSA adapter, verified locally on Ubuntu 24.04 x64 with PulseAudio and X11.
The other AudioStreamer Avalonia targets now have implementation heads; runtime verification remains pending;
see its [verification record](audio-verification.md) for the executed scope.

Linux reference adapters use C++ behind the reusable, language-neutral native module ABI.
PdfViewer now has two separate Linux examples: Qt with Qt PDF, and GTK with Poppler,
both implementing the same PDF channel contract through C++. Their runtime embedding and lifecycle checks are pending.

Milestone **0.2** requires an integrated, documented, and verified Avalonia host on **Android, iOS,
Windows, macOS, Linux, and browser/WebAssembly**. Every target must satisfy the criteria below.
Record supported OS versions, architectures, desktop backends, browser engines, and their tested
configurations; do not infer them from another host or target.

| Area | Completion criteria |
| --- | --- |
| Reusable host packages | Deliver reusable Avalonia host packages with framework-neutral plugin contracts. Keep Core independent of UI frameworks and require no MAUI dependency in an Avalonia consumer. Package each target's native or JavaScript assets and transitive dependencies. |
| Registration | Provide explicit module and native/DOM view-factory registration, DI integration, deterministic ownership, duplicate/conflict validation, and actionable startup errors. Reuse plugin configuration across hosts where its platform contract is the same. |
| Lifecycle | Integrate startup, shutdown, mobile suspension/resumption, window and view lifetimes, and browser page teardown. Dispose requests, registrations, subscriptions, callback references, and native/DOM resources; verify repeated attach/detach and reconnect without leaks or stale callbacks. |
| UI dispatch | Supply the host dispatcher for each target, including the browser UI context. Enforce UI-thread access for views, preserve handler ordering, and test asynchronous completion and reentrancy without blocking the UI or a serial queue on a remote reply. |
| Methods | Verify bidirectional method calls, typed arguments/results, successful null, missing/unimplemented endpoints, structured errors, cancellation, finite/infinite timeouts, and disposal against real adapters. |
| Messages | Verify bidirectional basic messages and acknowledgements, including binary payloads and supported codecs. Run shared protocol fixtures for numeric boundaries, buffer ownership, malformed data, and size/depth limits, including JavaScript interop. |
| Events | Verify independent subscriptions, initial/current state, bounded buffering and overflow, terminal signals, cancellation, listener cleanup, and restart without duplicate or stale events. |
| Native and DOM views | Embed a real platform view on each native target and a real DOM element in the browser host. Verify multiple instances, layout, input/focus, per-instance channels, UI dispatch, detach/reconnect, and invalidation of stale channels. Document supported view types and embedding limitations. |
| Reference-plugin integration | Integrate Currency, AudioStreamer, VolumeMonitor, and PdfViewer with Avalonia samples, reusing their managed contracts and platform adapters where compatible. Verify each available capability and explicitly report unsupported operations. If a platform lacks a plugin capability, use a clearly identified platform/browser example to exercise that channel contract; an unsupported plugin must not leave the contract untested. |
| Package-only consumers | Restore independent Release applications for all six targets from the final candidate packages with fresh caches and no library project references. Check native and JavaScript asset loading, dependency closure, trimming/AOT where applicable, and source/archive hashes. |
| Runtime CI | Execute all four channel contracts and lifecycle checks on Android, iOS, Windows, macOS, Linux, and a real browser/WebAssembly runtime. Use bounded jobs, reuse same-source build artifacts, and retain exact source/package evidence. Report simulator, desktop, browser, and physical-device results separately; compilation alone does not complete a target. |
| Documentation | Publish host setup, package installation, registration, plugin authoring, lifecycle, dispatch, channel/view examples, troubleshooting, and per-target capability/verification matrices. Clearly distinguish planned, implemented, compiled, executed, and unsupported behavior. |
| NuGet release acceptance | Audit the final versioned host/framework packages, licenses, dependencies, native/JavaScript assets, README content, and source metadata. Require successful package-only acceptance across all six targets and reviewed release notes. After separately authorized publication, verify clean NuGet.org restores and consumer smoke tests before recording the published release as accepted. |

Native API limitations must be explicit, but they cannot substitute for implementing the host,
all four channel contracts, view hosting, or runtime verification on a planned target. Preserve the
existing MAUI behavior and repeat its applicable regression and package-consumer checks when shared
contracts or packaging change.

### Browser and WebAssembly

The browser target requires its own packaged **JavaScript adapters** and .NET-to-JavaScript bridge,
with explicit module loading/registration and asynchronous calls in both directions. It must
support methods, messages, events, and DOM view factories using browser capabilities. Existing
Android, Apple, and Windows native adapters are not browser implementations.

Define and test the JavaScript boundary for exact integer values, binary buffers, errors,
cancellation, and callback/object lifetimes. Package scripts and other web assets so a consumer can
publish the application without copying files from the NativeChannels checkout. Use the host's
browser interop and document its loading and deployment requirements; Avalonia provides
[JavaScript interop](https://docs.avaloniaui.net/docs/platform-specific-guides/webassembly) for this environment.

Publish a capability matrix for each reference plugin and tested browser engine. In particular:

- Distinguish browser APIs and DOM elements from OS SDKs and desktop native controls. Document
  unavailable hardware, filesystem, or system-wide operations and return explicit unsupported errors;
  do not silently present a page-local alternative as an OS-wide capability.
- Document and test secure-context requirements, permission denial/revocation, user activation,
  and embedding restrictions for each selected API. Browser access depends on these conditions;
  see the [Permissions API](https://developer.mozilla.org/en-US/docs/Web/API/Permissions_API).
- For audio examples, require user interaction where autoplay is blocked and test the resulting
  suspended/resumed states. See the [browser autoplay rules](https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Autoplay).
- Document background-tab suspension/throttling, worker versus DOM dispatch, and teardown behavior.
  Verify the supported lifecycle instead of promising uninterrupted background execution.
- Record DOM layout, focus, accessibility, clipping, and layering constraints. Identify supported
  PDF/rendering implementations and their deployment assets rather than implying PDFKit or another
  OS view is available in the browser.

## Uno integration

Milestone **0.3** delivers complete Uno integration on **Android, iOS, Windows, macOS, Linux, and
browser/WebAssembly**. Apply every [Avalonia completion criterion](#avalonia-integration) to Uno:
reusable host packages, registration, lifecycle, UI dispatch, all four channel contracts, native/DOM
views, reference plugins, package-only consumers, runtime CI, documentation, and NuGet release acceptance.

Reuse shared contracts, transports, and platform/JavaScript adapters where compatible, while
implementing Uno-specific startup, dispatcher, view hosting, and packaging integration. The browser
host needs its own integration with Uno's [JavaScript and DOM hosting](https://platform.uno/docs/articles/interop/wasm-javascript-1.html)
and the same explicit capability limitations. Record each supported Uno backend and target
separately; Avalonia success is not Uno runtime evidence.

## Platform expansion

Treat each host/platform combination as an explicit implementation and verification target.
Desktop macOS in 0.2 and 0.3 is separate from the current MAUI Mac Catalyst integration; define and
verify its native bridge, view embedding, and packaging. Specify and test the supported Linux
windowing backends and native dependencies. Windows WinUI views likewise require compatible host
integration rather than being assumed portable to another UI framework. Browser support means
JavaScript/DOM integration, with the capability limits described above.

## Stable release

Use the [release checklist](release-checklist.md). Package publication and release creation remain
separate actions; completing a milestone does not publish to NuGet.org.
