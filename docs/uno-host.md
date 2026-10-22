# Uno host

`NativeChannels.Uno` is an optional host for Uno's Skia renderer, pinned to Uno
6.7.135. It adds application registration, per-window sessions, UI dispatch, and
an experimental native-view lifecycle surface. It references Core, Uno.WinUI,
and dependency injection; it does not require MAUI, Avalonia, Uno navigation, or
Uno application architecture packages. Register a compatible transport separately.

## Startup and shutdown

After creating your Uno application and window, on the window's UI thread:

```csharp
var host = app.UseNativeChannels(services => services.AddNativeChannels(options =>
{
    // Register the application's native modules here.
}));
var session = host.CreateSession(window);
var channels = session.Channels;
```

Each `CreateSession(window)` creates an independent service provider. Singleton
factories run per session; do not register a shared `IChannelService` instance.
Registration is snapshotted. A window's dispatcher is captured explicitly, so
multiple windows do not rely on a process-wide UI queue. Resolve channel services
initially on that queue.

Use `session.Dispatcher.DispatchAsync` as a channel handler dispatcher. It queues
asynchronous handler starts, propagates exceptions and cancellation, and reports
rejected queue submissions. Cancellation before execution prevents the handler
from starting; cancellation after execution starts cannot undo its side effects.

Await view disposal, then `session.DisposeAsync()`, **before** closing its window.
Await `host.DisposeAsync()` before terminating the application. Shutdown prevents
new sessions, cancels each session's `Stopping` token, and awaits owned services.
Repeated disposal shares the same completion. Failures in one session do not skip
other sessions. The host cannot guarantee UI cleanup after a dispatcher has stopped.
It deliberately does not launch unawaited shutdown from a synchronous `Closed` event.

## Experimental native views

`UnoChannelView` accepts a factory returning an `IUnoNativeView`. The factory owns
the native object and a distinct method channel for each instance. `NativeElement`
must be a public native wrapper supported by the selected Uno backend. The control
invalidates the old channel before destroying resources, supports reconnect, and
awaits asynchronous disposal. Explicit disconnect suppresses automatic reattachment
until `Reconnect` is called. Dispose views before their session.

`Connected` reports assignment to Uno's presenter, not verified native parenting,
visibility, input, or focus. The Windows adapter must supply child-window styles
before Uno reparents its HWND; see the [corrected Windows probe](uno-feasibility.md#windows-native-view-probe).
The host enables `WS_CLIPCHILDREN` on the attached HWND's actual parent and
invalidates the child once so software presentation cannot erase it. This flag
remains for the parent window lifetime and is shared by sibling native views.
For Windows root HWNDs, the host synchronizes native focus with the managed tab
position and routes Tab/Shift+Tab through Uno's focus manager. Reconnect restores
focus after the replacement child is shown; removing a focused view advances
focus before destroying the HWND. Each root HWND is one tab stop. Compound native
controls must manage traversal within their own child HWND trees; that case is
not covered by this bridge. This does not provide PDF document accessibility.
Native parenting, layout, focus and direct input-message checks pass on Windows.
Desktop-tool visual review confirms buttons and PDF content remain visible;
broader physical input and accessibility coverage remain outstanding.

macOS uses `MacOSNativeView`, an adapter-owned AppKit container around Uno 6.7.135's
internal native element. Reflection and native lifetime compatibility are isolated
in the optional Uno package. The shared Swift view factories remain unchanged.
TODO: replace this bridge when [Uno #25639](https://github.com/unoplatform/uno/issues/25639)
provides public NSView hosting. Consumers do not need a patched Uno build or their
own reflection code. This compatibility path is verified against the pinned release
in untrimmed desktop builds; trimming/AOT and other Uno versions are not validated.
Dispose on the UI thread after detaching the presenter, before releasing the borrowed
NSView. The wrapper retains the view while hosted and does not own its plugin callbacks.

This package does not establish six-platform Uno support. See
[the feasibility matrix](uno-feasibility.md) for the remaining acceptance work.

## Verification

On Windows, run `pwsh -File eng/verify-uno-feasibility.ps1 -HostProbe`. The consumer
uses local NuGet packages and the real Uno UI event loop. It tests registration,
independent window sessions, asynchronous and reentrant dispatch, queued cancellation,
handler exceptions, view lifetime, and shutdown with a pending call and active
subscription. The view fixture uses a managed element; it does not test native
parenting. `-ReuseCache artifacts/uno-feasibility/cache` is available for diagnostic
reruns and is recorded as reused-cache evidence. No UI host runtime result on other
platforms is implied by this Windows test.

For macOS Interop and PDF parity, run `python3 eng/verify-uno-macos-views.py` on the Mac
with `DOTNET_HOST_PATH` set to the pinned SDK. It builds ARM64/x64 native libraries,
packs the adapters, and runs the existing MAUI/Avalonia verifiers on the host architecture.
`--reuse-cache <directory>` hard-links external packages only; local packages are restored
fresh. Evidence, package hashes and released-Uno runtime checks are retained under
`artifacts/uno-macos-views/<run>/`. Physical input and visual acceptance remain separate.
