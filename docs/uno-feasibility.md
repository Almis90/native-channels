# Uno 0.3 feasibility

Status: **experimental implementation; six-platform feasibility is not established**.
Steps 2â€“6 added the portable desktop transport, optional host, shared plugin
integration, five sample apps and build/audit automation. Native-view and PDF
acceptance remain open. The results below retain their original scope; see
[0.3 preparation](preview-03.md) for the current matrix and release gates.

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

The separate `uno-macos-embedding` checkout now contains a proposed public
`MacOSNativeView` wrapper against upstream commit
`503f37142d4c7055fb8c2f75da819a7ab9ee6993`. Five focused runtime tests passed on
the ARM64 Mac, including native callbacks, reconnect/reparent, ownership cleanup
and independent PDFKit views. The patch remains local for review; it is not
installed into this project's pinned Uno packages and no upstream PR is open.
This is a missing public capability, not proof that embedding without an Uno
change is impossible. Reflection or application-owned native hosting may be
possible, but would put compatibility and lifecycle maintenance on this project;
those alternatives have not been validated. Keep that distinction when deciding
whether to propose the API upstream.

#### MacBook revalidation: October 21, 2026

Ran the existing `eng/verify-uno-apple-desktop.py` against committed source
`e6b24e8` in an isolated checkout on the ARM64 MacBook, using SDK 10.0.401,
Xcode 26.6 and macOS 26.2. The probe restored into a fresh package cache and
built with zero warnings/errors. Native assets compiled for ARM64 and x64;
only ARM64 executed. Native Currency returned EUR's symbol, and all three
existing desktop-contract sessions passed callbacks, messages, events,
cancellation/reuse and disposal.

The restored macOS runtime still reports `MacOSNativeElement` and
`MacOSNativeElementHostingExtension` as `NotPublic`. The matching source also
declares [`ContentPresenter.INativeElementHostingExtension` as internal](https://github.com/unoplatform/uno/blob/da217df2312e9e39676880f9cb4b8d519cc297e0/src/Uno.UI/UI/Xaml/Controls/ContentPresenter/INativeElementHostingExtension.cs),
so registering our own implementation is not a public extension route either.
This confirms a missing integration API in the pinned Uno release. A supported
upstream wrapper/extension is still needed before the AppKit and PDFKit sample
scenarios can run; the transport pass does not establish native-view support or
full five-app coverage. No reflection workaround or dependency upgrade was added.

Evidence on the Mac is under
`/Users/almis/Projects/plugins/native-channels-uno-03/artifacts/uno-feasibility/runs/cbb575147f9846a18c2c042c0c11fa7f/`.
The dependency report, contract log and package hashes were also copied to local
`artifacts/uno-feasibility/mac-20261021/`. Next, validate the Currency sample on
macOS using its existing shared verifier while the native-view API remains
blocked. The Mac had about 1.9 GiB free after this fresh-cache run; recover build
cache space before further broad restores.

#### Currency sample execution on macOS

Removed only the two dependency-cache directories from the earlier
`/tmp/nativechannels-uno-step2-00d61d9` probe, retaining its reports and packages.
Free space increased from 1.9 to 8.2 GiB. The current probe cache and freshly
built native packages were reused for the ARM64 Currency sample.

The actual Uno Currency app built with zero warnings/errors and ran the same
`samples/Currency.Shared/PluginVerification.cs` used by MAUI and Avalonia. Native
lookups, normalization, structured errors, concurrency and cancellation passed
with the `apple` platform expectation. The result file reported `checks=passed`
and the process exited normally with code 0 within the 45-second bound, including
the shared page/session cleanup and window-close path. No new test scenarios
were added. The sample's limitation text now includes macOS ARM64.

Build/runtime logs and the result are retained in the Mac checkout's
`artifacts/uno-feasibility/currency-macos-*` files and copied to local
`artifacts/uno-feasibility/mac-20261021/`. Next is AudioStreamer on macOS with
the existing shared scenarios. Native-view/PDF support remains blocked as above.

#### AudioStreamer sample execution on macOS

The ARM64 Uno AudioStreamer app built with zero warnings/errors using the same
fresh native packages and existing cache as Currency. The shared MAUI/Avalonia
PCM verifier passed playback progress, bounded buffering, pause/resume,
cancellation, exact drain counts and restart. The existing shared host verifier
also passed three session lifetimes, pending-write disposal, DI cleanup and
rejection after disposal. The Uno control path passed pause/resume, explicit
suspension, restart and stop, followed by page cleanup with paused playback and
normal window close (exit 0 within the 45-second bound).

On macOS this control check invokes `SuspendAsync` directly; it does not minimize
the window. The result log now distinguishes `suspend/restart` from Windows'
actual `minimize/restart`. No additional scenarios were introduced. Audible
playback and actual macOS background/minimize event delivery remain unverified.
Evidence is in `artifacts/uno-feasibility/audio-macos-*` on the Mac and copied to
local `artifacts/uno-feasibility/mac-20261021/`. Next is VolumeMonitor on macOS
with the existing shared subscription verifier.

#### VolumeMonitor sample execution on macOS

The ARM64 Uno VolumeMonitor app built with zero warnings/errors against the same
native package feed and cache. The unchanged MAUI/Avalonia subscription verifier
passed initial levels for two listeners, independent cancellation, and restart.
The actual sample wrote `checks=passed` and exited normally with code 0 within
the 45-second bound, including page/session disposal and window close.

The limitation text now includes macOS ARM64 subscriptions. Hardware volume
changes remain unverified, as stated by the shared verifier; no extra scenarios
were added. Build/runtime logs and the result are retained as
`artifacts/uno-feasibility/volume-macos-*` on the Mac and copied to local
`artifacts/uno-feasibility/mac-20261021/`.

Currency, AudioStreamer and VolumeMonitor have now executed their shared checks
on macOS ARM64. Interop's native views and native PDF remain blocked by the
missing public Uno macOS hosting API described above. The next macOS step is
resolving that integration path before claiming all five samples pass.

#### Upstream hosting investigation: October 21, 2026

NuGet's stable version lists still end at Uno.Sdk 6.7.30 and Uno.WinUI 6.7.135,
matching our pins. The current upstream source at
[`503f37142d4c7055fb8c2f75da819a7ab9ee6993`](https://github.com/unoplatform/uno/blob/503f37142d4c7055fb8c2f75da819a7ab9ee6993/src/Uno.UI.Runtime.MacOS/Native/MacOSNativeElementHostingExtension.cs)
also keeps the macOS wrapper and host extension internal (the runtime directory
has been renamed). Its `ContentPresenter.INativeElementHostingExtension` remains
internal as well. Neither a stable upgrade nor that inspected source revision
provides the missing public integration route.

A minimal compile probe on the Mac, referencing the actual restored sample
assemblies, produces CS0122 for both types. It is retained in
`artifacts/uno-feasibility/hosting-repro/` with `hosting-compile.log` on the Mac;
copies are under local `artifacts/uno-feasibility/mac-20261021/`. This is diagnostic
evidence of the API boundary, not an additional sample acceptance requirement.

Simply changing the wrapper's visibility would be insufficient: its handle
setter is internal, and the native host expects an Uno-specific
`UNONativeElement` peer with parent and disposal behavior, not an arbitrary
`NSView`. Ownership must also distinguish detachment from final destruction.
The related upstream [reparenting issue #24172](https://github.com/unoplatform/uno/issues/24172)
and [native-peer fix #24112](https://github.com/unoplatform/uno/pull/24112) describe
that lifecycle concern; the inspected current source contains lifetime fixes but
still lacks the public wrapper API.

The concrete upstream request below is prepared for review, not posted. A
maintained Uno fork would be a separate dependency/maintenance decision; no fork,
private API access, reflection integration or dependency change was introduced.

##### Draft upstream request: public AppKit view embedding for Skia macOS

**Problem:** A third-party control library cannot place an adapter-owned AppKit
view, such as a button or PDFKit `PDFView`, inside a Uno `ContentControl` on
macOS. Equivalent public native wrappers exist on Windows and X11. On stable
Uno.Sdk 6.7.30 / Uno.WinUI 6.7.135, even referencing the macOS wrapper or the
hosting extension contract fails to compile:

```csharp
public static class HostingRepro
{
    public static System.Type Wrapper =>
        typeof(Uno.UI.Runtime.Skia.MacOS.MacOSNativeElement); // CS0122
    public static System.Type Extension =>
        typeof(Microsoft.UI.Xaml.Controls.ContentPresenter.INativeElementHostingExtension); // CS0122
}
```

Reproduce in a `net10.0` class library referencing the `Uno*.dll` files from a
built Uno macOS desktop sample. On our ARM64 Mac, both expressions fail with
"inaccessible due to its protection level"; the sample's native messaging calls
work, isolating this from transport initialization.

**Requested API:** A supported public macOS wrapper/factory accepting an AppKit
view handle and defining ownership, analogous to the existing native wrappers.
Uno should supply the native container/peer that participates in its rendering
hierarchy, layout and clipping. Third-party adapters should not need to implement
Uno's private Objective-C protocol or register a replacement global host.
Define UI-thread creation/disposal, whether the supplied view is borrowed or
retained, and when final release occurs. Detach/reparent must not destroy a peer
that can be reattached. The adapter must be able to release callbacks and destroy
its own view deterministically after detachment without double release.

**Validation scope:** Reuse our existing MAUI/Avalonia sample scenarios: two
independent native views, layout/clipping, callbacks, detach/reconnect,
unload/reload and cleanup; then native PDF open, navigation, zoom, independent
viewers and lifecycle. No new certification gates are requested.

**Consumer impact:** Once a supported API exists, NativeChannels can implement
the AppKit/PDFKit adapters behind its optional Uno host. Consumers should keep
using the plugin API without private Uno workarounds. Until then, macOS native
view/PDF support remains unavailable; the three messaging plugins are separately
validated.

### Windows native-view probe

Update on October 20: the attachment failure was traced to the probe adapter's
`WS_POPUP` window style. Uno reparents the HWND but does not convert it to a child
window. The adapter now clears `WS_POPUP` and sets `WS_CHILD | WS_TABSTOP` before
handing the wrapper to Uno, following the Windows
[SetParent contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent).
Both initial attachment and recreation now pass. No Uno internal API or host
workaround is needed. The diagnostic reports both `GetParent` and `GetAncestor`
alongside the actual style bits, so an owner/parent mismatch is visible.

The Interop sample additionally checks two real native controls: separate HWNDs
and channel identities, native bounds, resizing one without changing the other,
show-style flags, focus transfer, direct mouse/keyboard message delivery,
reconnect isolation, stale channels and native handle destruction. The app is
launched hidden; these are structural and message-dispatch checks, not screenshot
or physical-input verification. Manual visual/input acceptance remains open.

Historical result before this correction:

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
  returns `â‚¬`. Three successive native sample sessions pass method callbacks,
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

## Step 4: shared plugins and PDF control

The three messaging plugins now expose both Windows and Linux factories in their
portable desktop assemblies, with optional macOS dylib packaging. The Windows
package-only Uno probe executes the real Currency, AudioStreamer and VolumeMonitor
adapters, including active-resource shutdown. `Plugin.Uno.PdfViewer` adds the
control and adapter contract, with tested state/callback/reconnect semantics.
It requires an explicit factory and has no bundled native PDF adapter yet.

Step 4 remains partial: native PDF integration and the other platforms' plugin
acceptance are outstanding. See [Uno plugin integration](uno-plugins.md) for exact
test scope and lifecycle responsibilities.

## Steps 5â€“7: samples, automation and release documentation

Subsequent Windows PDF integration adds a default Windows.Data.Pdf adapter inside
an Uno-hosted child HWND. It reuses the existing Windows document source without
a MAUI/Avalonia package dependency. Native page/zoom pixel checks, invalid input,
reconnect and pending-request cleanup now pass; the managed protocol fixture
remains a separate check. See [PDF setup and limitations](../plugins/Plugin.Uno.PdfViewer/README.md).

Five separate Uno sample apps now share startup, shutdown and verification code.
The unified runner repacks into an isolated feed, audits package metadata and
restored archive origin/bytes, and runs all five Windows smoke checks. The local
step-6 run passed with managed protocol/lifecycle fixtures for Interop and PDF.
The subsequent native PDF run passes its runtime projection audit and rendering
checks. An intermittent VolumeMonitor shutdown timeout was traced with a process
dump to Uno's UI thread joining its render thread while that thread was blocked
in a Skia GPU flush. The five Windows sample hosts now explicitly select Uno's
public software rendering backend. This avoids the affected GPU shutdown path
without changing volume observation or forcing process termination. GPU rendering
acceptance remains outstanding on the pinned Uno runtime.
Desktop CI definitions cover Windows, Linux and macOS
builds; no execution of those CI jobs is claimed here.

Step 7 documents current behavior and prepares unreleased notes. Release versions
and publication remain gated on the unfinished acceptance below. See the
[sample instructions](../samples/Uno/README.md) and [release status](preview-03.md).

## Windows visible review (October 21, 2026)

The shutdown workaround is committed as `c0a4935`. A subsequent visible review
used the Windows computer-use capture/input tools against the Interop and PDF
samples. In Interop, clicking **Show two native Windows controls** attached both
HWNDs, but the captured view showed their content only briefly before the reserved
area became blank. In PDF, **Open PDF** with the pinned runtime reported page 1/3
at zoom 1 while the captured page area became blank. These observations do not
establish whether the remaining issue is native composition, repainting, or the
capture path. Stable visible rendering and mouse/keyboard acceptance remain blocked.

A parent `WS_CLIPCHILDREN` experiment and explicit content-stretch settings did not
resolve the Interop observation; neither change was retained. Added native-region
checks reject an explicitly empty HWND clipping region. Both targeted hidden
probes still pass those checks and the existing lifecycle/painting checks; they
must not be interpreted as resolving the visible failure. Evidence is recorded
locally in `artifacts/uno-feasibility/visible-Interop-clip.json` and
`artifacts/uno-feasibility/visible-PdfViewer-clip.json`.

Launching PDF directly through the desktop tool selected system .NET 10.0.5 and
crashed in Uno's `Win32UIAutomationInterop.UiaRaiseStructureChangedEvent` (Windows
Application events 1000/1026). Relaunching with the repository `DOTNET_ROOT` opened
the document without that crash during this review. Keep the runtime distinction
explicit; the system-runtime crash has not been fixed or fully isolated.

### Investigation and correction

The follow-up isolated a parent-painting problem: child HWNDs had nonempty bounds
and clipping regions, but the actual parent lacked `WS_CLIPCHILDREN`. Uno's
software renderer blits into that parent DC and could erase the native pixels.
Setting the flag on `GetParent(child)` and invalidating the child restored both
controls in the same capture/input tool. This rules out capture alone as the cause.

`UnoChannelView` now prepares the actual native parent after attachment (retrying
on layout if Uno has not parented it yet), preserves existing style flags, and
invalidates the child once. Reconnect resets the preparation state. The parent
flag remains for the window lifetime because multiple native views share it.
This uses public Uno wrappers and Win32 APIs, without changing the renderer or
putting Windows behavior into the portable transport.

Fresh package run `artifacts/local-samples/uno/windows/20261021-121359-c49811ee`
passed all five builds and smoke tests, including new parent-style assertions in
Interop and PDF. Visible review of those packaged binaries confirmed stable
buttons, mouse toggling, second-control independence, reconnect and maximized
layout. Space-key toggling was also confirmed in the diagnostic build. PDF
showed the guide, page 2, zoom 1.5, reconnect/reopen and document removal correctly.
Inputs were injected through the desktop computer-use tools, not by a person
operating a physical mouse/keyboard.

The blank-content blocker is resolved in the tested Windows software path.
PDF drag/wheel attempts initially did not visibly pan the zoomed page. The
follow-up isolated this to our PDF adapter's `STATIC` HWND missing `SS_NOTIFY`:
its default hit test returned `HTTRANSPARENT`, routing mouse input to the parent.
The adapter now enables `SS_NOTIFY`, takes focus and capture on drag, and clears
drag state on release, capture loss or cancellation. Consumers need no workaround.
The new hit-test regression failed before the fix. Fresh-package run
`artifacts/local-samples/uno/windows/20261021-122502-d57bb59f` passed PDF checks for
hit testing, focus, drag, vertical/Shift-horizontal wheel pixel changes, capture
release and cancelled drags. Desktop-tool input also visibly moved the zoomed
page by dragging and scrolling; document removal still worked.

That run passed all five builds and four smoke tests, but Interop failed its
existing empty-clipping-region assertion. An isolated retry of the same binary
passed (`artifacts/uno-feasibility/interop-input-followup.json`); that full run is
not recorded as passing.

The clipping-test follow-up replaced its single check after a fixed 250 ms sleep
with a bounded readiness check: both live, attached, shown HWNDs must have
positive bounds and nonempty (or unrestricted) clipping for 250 ms, within five
seconds. The same check now runs after resize and reconnect. Timeout diagnostics
include each HWND, parent, bounds, region type and clip rectangle. Ten unchanged
baseline runs passed, so the original intermittent failure was not reproduced;
late render readiness is a hypothesis, not a confirmed production root cause.
Ten runs of the hardened check also passed. In a temporary diagnostic build,
forcing empty native regions for 600 ms recovered and passed; forcing them
permanently failed with the expected region diagnostics. Injection code was
removed before rebuilding. Evidence is in
`artifacts/uno-feasibility/interop-clipping-{baseline,stable}-*.json` and
`interop-clipping-{delayed,permanent}.json`. This hardens the test without changing
production clipping or suppressing persistent empty regions.
The first fresh-package follow-up (`20261021-123337-b6b19d76`) passed all five
builds/smoke tests, but the runner rejected acceptance because these documentation
edits changed the source snapshot during execution. A new run must use a stable
source snapshot; this run is diagnostic evidence only.

The interactive PDF window initially remained open with disabled controls after
Alt+F4. This was isolated to the shared sample close handler: disposal could
complete synchronously, so its final `Window.Close()` reentered the original
`Closed` callback before Uno had finished processing the handled close request.
Cleanup and the final close are now queued on the window dispatcher, with repeated
close requests coalesced while disposal is pending. The PDF baseline wrote a
passing result but remained alive for 15 seconds when the smoke test used normal
window close; with the fix it exited with code 0. Desktop-tool Alt+F4 also closed
the visible window and process with a rendered document still open. Evidence:
`artifacts/uno-feasibility/pdf-close-{baseline,fixed}.json`; the baseline JSON alone
does not record its process timeout. All shared sample smoke tests now call
`Window.Close()` instead of bypassing the handler with `Application.Exit()`;
the runner requires process exit as well as a successful result file.
Broader physical
input/accessibility, GPU rendering and other-platform acceptance remain open.

## Remaining gates

### Acceptance scope: existing MAUI and Avalonia scenarios

For 0.3, Uno acceptance must follow the existing MAUI and Avalonia sample
scenarios. Do not add Uno-only features or certification requirements. Existing
bug regression checks may remain, but do not expand the acceptance checklist.
This scope supersedes the broader review requirements in the historical notes.

The PDF comparison is based on `samples/Maui/PdfViewer/PdfVerification.cs`,
`samples/Avalonia/PdfViewer/Shared/PdfPage.cs`, and
`samples/Uno/PdfViewer/NativePdfProbe.cs`:

| Scenario | MAUI | Avalonia | Uno native Windows |
| --- | --- | --- | --- |
| Open bundled three-page document, initial page/state | Yes | Yes | Yes |
| Page navigation and zoom | Yes | Yes | Yes |
| Two independent native viewers and state isolation | Yes | Yes | Yes |
| Disconnect, reconnect, reopen with fresh identity | Yes | Yes | Yes |
| Invalid page/open preserves document | Yes | Not in sample verifier | Yes |
| Native callback on UI thread | Explicit check | Not explicitly checked | Not explicitly checked in native PDF probe |
| Disconnect with work pending | Yes | Not in sample verifier | Yes |

MAUI and Avalonia themselves do not have identical verifiers. Use scenarios
already present in either as the scope boundary, and report differences instead
of claiming exact coverage. For the other plugins, compare their corresponding
sample checks before adding any Uno requirement.

Screen-reader certification, global UIA focus-report certification, OCR/text
extraction, expanded PDF document corpora, and DPI/GPU/physical-device campaigns
are not additional Uno 0.3 acceptance gates. Keep unverified behavior documented;
do not infer validation from excluding it from this scope.

### Interop parity: Windows

Uno now links the same `NativeVerification.cs` used by MAUI and Avalonia and
runs it against the shared Windows sample module through NativeChannelTransport.
It replaces the two in-memory echo checks with the existing native suite: typed
values/DTOs/bytes/null/Int64, callback/UI dispatch, structured errors, 16 concurrent
calls, cancellation/timeouts, handle/session disposal, bidirectional message
codecs, and event completion/errors/independent subscription cleanup.

Uno also links Avalonia's `ViewVerification.cs` and Windows native button adapter.
Framework-specific branches only supply the dispatcher, HWND wrapper, initial
parenting and layout properties. The assertions remain shared: two instances,
creation arguments, acknowledged UI-thread callbacks, state isolation, pending
work disposal, reconnect, detach, unload/reload, stale channels, off-thread
rejection, and 32 reconnect cycles. Existing Uno host and HWND regressions remain.
These are existing sample scenarios, not new Uno acceptance requirements.
Other Uno platforms are still unverified; this comparison establishes Windows
coverage only.

### Currency parity: Windows

Uno now links `samples/Currency.Shared/PluginVerification.cs`, the same verifier
used by MAUI and Avalonia. It checks concurrent USD/EUR/GBP/JPY lookups, nonempty
symbols, exact EUR, whitespace/case normalization, the unknown-code error's code,
platform and details, invalid-length rejection, pre-cancellation and a successful
lookup after cancellation. Uno selects the expected platform at runtime because
its desktop target framework is shared across operating systems; assertions are
unchanged. Windows execution is verified separately from the other pending Uno
platforms.

### AudioStreamer parity: Windows

Uno links MAUI's `AudioVerification.cs`, already reused by Avalonia, with unchanged
assertions for silent PCM playback progress, bounded buffering, paused-write
cancellation, resume/drain with exact frame counts, and clean restart. It also
links Avalonia's host-disposal verifier; only host/session creation differs.
Three sessions check worker-thread disposal with a pending paused writer,
DI-owned cleanup, repeated disposal and rejection after disposal.

The Uno Play/Pause/Resume/Stop commands are shared with its silent control check,
matching Avalonia's desktop scenarios. Minimizing now invokes the existing
suspension cleanup path. The check minimizes/restores the actual sample window,
verifies stopped playback, restarts and stops, then leaves paused playback for
the shared runner's page disposal and window close. These are existing Avalonia
scenarios, not additional certification requirements. Listening and non-Windows
execution remain unverified.

### VolumeMonitor parity: Windows

Uno links MAUI's `VolumeVerification.cs`, already reused by Avalonia, without
changing its assertions: two listeners receive initial levels, cancelling one
does not end the other, subscriptions cancel, and a restarted listener receives
an initial level. The Uno controls match Avalonia's system-volume example: add
independent listeners, cancel the last listener, and stop all listeners. Existing
page suspension and disposal stop all listeners.

Hardware volume changes remain a separate manual check, as in the shared
verifier. Non-Windows execution and Avalonia's browser-only page-owned audio
example are not verified by the Windows run. All five Uno Windows samples now
have a documented comparison with the existing MAUI/Avalonia scenarios; the
remaining work is platform validation within that same scope.

### Windows keyboard and accessibility review (October 21, 2026)

Reviewed the packaged Windows software-rendered Interop and PDF binaries from
`artifacts/local-samples/uno/windows/20261021-124000-29dc9baf` using the repository
runtime (`DOTNET_ROOT=.tools/dotnet`). Desktop-tool mouse/key injection and UI
Automation inspection are not physical-device or screen-reader acceptance.

| Check | Observation | Result |
| --- | --- | --- |
| Native checkbox discovery | Both native controls appear as check boxes in the UI Automation tree. | Passed discovery only |
| Native activation | Clicking the first checkbox focuses/checks it; Space clears it, with the second unchanged. | Passed injected input |
| Native keyboard traversal | With the first checkbox focused, Tab and Shift+Tab both leave focus on that checkbox. | Blocked: focus trap |
| Native accessible names | Both controls are named `Native Win32 button`. | Needs distinct sample labels |
| PDF controls/status | Path field, command buttons and `Page 1/3` through `Page 3/3` status appear in the accessibility tree. | Passed discovery only |
| PDF page content | Native surface appears as `text PDF`; no rendered document text is exposed in the inspected tree. | Unverified; outside parity scope |
| PDF focus reporting/traversal | After clicking the native page, UI Automation still reports the path field as focused; Tab does not advance to the next surrounding control. | Blocked: focus integration |
| PDF visible navigation | All three guide pages render at 1x; maximizing preserves the page. | Passed for bundled guide |

Reproduce the native focus trap by launching Interop, choosing **Show two native
Windows controls**, clicking the first checkbox, and pressing Tab then Shift+Tab.
Space still toggles the checkbox. Reproduce the PDF observation by opening the
guide, clicking its native page, then pressing Tab and inspecting focus. These
findings do not yet establish whether Uno or our host should supply the missing
focus bridge; direct `SetFocus`/input-message smoke tests do not cover traversal.

The follow-up adds a Windows root-HWND focus bridge in `UnoChannelView`. Native
focus updates Uno's logical focus; managed entry focuses the native HWND, and
Tab/Shift+Tab routes through the window's managed tab order. Reconnect recreates
the presenter to force native layout and restores focus once the child is shown.
Disconnection advances focus and removes the native subclass before destruction.
Interop now uses distinct first/second checkbox names. Regression checks cover
managed/native focus synchronization, forward/reverse traversal, focused reconnect
and focused removal. A diagnostic run passed after reproducing a replacement-HWND
layout failure with the new focused-reconnect test.

Desktop-tool checks confirmed first-to-second native Tab, native-to-managed Tab,
and managed-to-native Shift+Tab. PDF Tab advances from the page to the path field
and then Open PDF. The PDF accessibility tool still reported the path field while
the visible focus border was on Open PDF, so accessibility focus reporting is not
certified. Compound native controls with child HWNDs are outside this root-HWND
bridge's verified scope.

The accessibility and broader document/device observations above are recorded
limitations, not additional Uno-only acceptance gates. Continue with the existing
MAUI/Avalonia scenarios listed in the scope table.

### PDF accessibility provider follow-up

The Windows raster surface now supplies a native UI Automation provider rather
than relying on the default STATIC text proxy. It exposes a Pane role, current
page/count/zoom name, focusability, actual HWND focus, bounds and fragment focus
lookup, with focus/name-change notifications and provider disconnection during
disposal. No application-specific workaround is needed for these properties.

The new sample regression uses a real COM UIA client on an MTA worker to query
the owned native HWND while the UI thread services requests. The baseline fails
with the old generic name `PDF`; the diagnostic provider build passes name/role,
unfocused and focused properties, and page navigation checks. Local evidence:
`artifacts/uno-feasibility/pdf-uia-{baseline,provider}.json`. Desktop inspection
also shows the updated page/zoom name, but its global focused-element report
continues to name the path field. Managed-peer/focus-event experiments did not
resolve that observation and were removed. Correct element properties do not
establish correct global focus events or screen-reader behavior.

Microsoft describes [Windows.Data.Pdf](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf)
as an image-rendering API. The provider deliberately does not advertise
TextPattern. Text extraction and OCR are outside the current sample scope,
matching MAUI and Avalonia. Screen-reader behavior and global focus reporting
remain documented limitations, not Uno-only release checks. Native provider integration
follows the documented
[server-side UIA provider contract](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider).

### Outstanding platform work

#### iOS messaging validation: October 21, 2026

Executed the three Uno messaging apps on the iPhone 17 Pro simulator, iOS 26.5,
ARM64, with Xcode 26.6 and .NET SDK 10.0.401. `eng/verify-uno-ios.py` packs the
Apple binding, transport, optional Uno host and selected plugin into a fresh
per-run feed. It rejects library project references and verifies each restored
local package's origin and bytes against that feed. Third-party dependencies
were reused. The existing production transport and plugin code needed no change.

| Sample | Runtime result | Existing scenarios exercised |
| --- | --- | --- |
| Currency | Passed | Native symbols, normalization, structured errors, concurrency, cancellation and reuse |
| AudioStreamer | Passed | Silent PCM, buffering, pause/resume, cancelled writes, drain/restart, three host sessions, controls and disposal |
| VolumeMonitor | Passed | Initial output levels, independent listeners, cancellation and subscription restart |

Mac evidence is under `artifacts/uno-ios/` in these runs:
`847c8c19284b4bccb516ba5ce91ca445` (Currency),
`7a52300d0e0841eeba52c98e0d101160` (AudioStreamer), and
`ff9d86f61c28451ea79755798a3c0621` (VolumeMonitor).
Logs, summaries, resolved assets and package hashes were copied locally to
`artifacts/uno-ios/{currency,audio,volume}/`. Source was `06bb92e` plus the
uncommitted runner; sample limitation labels were updated after execution.
The separate macOS Uno API patch was not used in these runs.

The first launch failed before managed startup because the runner disabled
signing. The crash report recorded `CODESIGNING / Invalid Page`; normal SDK
simulator signing fixed it. All three final builds had zero errors and two
existing trimming warnings from the sample's JSON result serialization.
All checks ran in Debug. No physical-device, audible-playback, hardware-volume,
or actual OS background-transition acceptance is inferred; AudioStreamer calls
the existing suspension cleanup directly. No additional verifier scenarios
were added, and these separate feeds are not a combined release candidate.

Interop is not counted as an iOS pass: its shared native method/view verifier
is included only for desktop and invoked only on Windows. PdfViewer explicitly
rejects non-Windows native verification. Next wire the existing Apple module
and UIKit view checks into Uno Interop, then integrate the existing PDFKit
backend into the Uno PDF control. These are our integration gaps, not evidence
of an iOS Uno framework defect.

#### Remaining work

The subsequent iOS Interop run also passed both existing shared suites. Uno now
links MAUI's `NativeVerification.cs`, Avalonia's `ViewVerification.cs`, and the
existing Apple `MobileButtonView` adapter. The framework branches select Uno's
dispatcher, `IUnoNativeView`, and the UIKit `UIView` content; assertions are
unchanged. The sample has the same explicit friend-assembly access to our
native-view factory as Avalonia's Interop sample. No Uno dependency change,
reflection workaround, or new production view API was needed.

The simulator log confirms typed DTO/byte/null/Int64 calls, errors, UI callbacks,
16 concurrent calls, timeout/cancellation, handle/session disposal, all four
message codecs and event cleanup. Native views pass two-instance independence,
creation arguments, acknowledged callbacks, pending-work disposal, reconnect,
unload/reload, stale-channel rejection, UI-thread enforcement and 32 reconnects.
Only these existing shared verifiers run for iOS; the desktop-specific host and
Win32 regressions remain on their existing path. The Windows Interop build also
passes with zero warnings/errors after the shared-file changes.

Initial successful Mac evidence is
`artifacts/uno-ios/c80c3c697d6942ef9b6aa106f374b290/`; final local evidence is
retained at `artifacts/uno-ios/interop/`. The first compile attempt caught a
dispatcher delegate mismatch and missing sample friend-assembly access; both
were corrected before runtime testing. Debug simulator limits still apply.
The subsequent iOS PdfViewer run also passed. The thin Uno adapter reuses the
existing `MauiPdfViewerNative` PDFKit framework without a MAUI dependency. The
sample links MAUI's `PdfVerification.cs`; only framework aliases differ, with
no additional assertions. It verifies the three-page document, page navigation,
2x zoom, UI callbacks, independent instances, errors preserving the document,
reconnect, pending-work detach and reopening.

Integration caught two issues in our control/package: the iOS library must use
Uno's Skia UI assets to match the host, and both nested content controls must
stretch the native PDF view. Both are handled in the package; consumers need no
workaround or Uno patch. Evidence is retained locally at `artifacts/uno-ios/pdf/`.
The iOS build has two existing JSON trimming warnings and no errors. All five
iOS samples now pass their shared automated scenarios on the Debug ARM64
simulator. Physical-device input, visual acceptance and combined release
packaging remain outstanding.

1. Continue comparison against existing MAUI/Avalonia sample scenarios. The
   native PDF probe now checks two independent viewers. Keep completed native parenting, layout/resize
   and direct input-message regressions; do not add Uno-only certification gates.
2. Extend desktop package acceptance into production plugin packaging and release
   CI, retaining the plain-.NET macOS bridge outside the UI host package.
3. Establish a supported macOS native-element extension path against this pinned
   release, or identify the upstream change needed. Do not silently reduce the
   six-target native-view acceptance requirement.
4. Complete Linux/browser probes and remaining mobile runtime acceptance with fresh
   package feeds. Record each backend, architecture, package
   hash and execution result separately.

#### Android validation: October 21, 2026

All five Uno apps passed their existing shared MAUI/Avalonia verifiers on the
API 36 x64 emulator with fresh local Android packages. The native Kotlin
implementations and verifier assertions are unchanged. Interop links the
existing Android sample AAR and native-view adapter; PDF reuses the shared
PdfRenderer AAR through the thin mobile Uno adapter. No Uno source patch or
consumer workaround was needed.

The Uno templates needed API 24 generally and API 31 for AudioStreamer, matching
the existing adapters and MAUI/Avalonia samples. The runner also needed to wait
for activity startup before collecting its process ID. Currency's first launch
attempt was interrupted by that runner timing error; the same built APK passed
after the runner fix. Its successful result is retained separately from the
failed launch report.

Evidence under `artifacts/uno-android/`:

| Sample | Run | Result file |
| --- | --- | --- |
| Currency | `ec4fbc93ad304d21b234e7436ece2809` | `runtime-retry.json` |
| Interop | `281da44c2ab646e095718964c072f012` | `summary.json` |
| PdfViewer | `3dac870fb6bf4c6e876d33ff1c5b8761` | `summary.json` |
| AudioStreamer | `7c7f035eeef445ed8854d96261ac5706` | `summary.json` |
| VolumeMonitor | `fa221d81023b440ea0beebff87a22310` | `summary.json` |

Reports retain package/APK hashes, package provenance and source status. Builds
used the working-tree changes recorded in each report. All five app builds had
zero warnings/errors; Android binding packaging emitted BG8605/BG8606 warnings.
The PDF package audit confirms an Android assembly and native PDF AAR without
MAUI/Avalonia dependencies or Windows projection DLLs. Windows PDF plugin and
Interop sample builds also passed with zero warnings/errors after these changes.
This covers Debug x64 emulator execution only. Physical-device/ARM64, visual
and input acceptance, audible playback, hardware volume changes, actual OS
background transitions and combined release packaging remain outstanding.

Keep full native-view/PDF and release acceptance gated on these findings.
No runtime support is inferred from template generation or source inspection.
