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
visibility, input, or focus. This surface is experimental: the Windows feasibility
probe has not established native attachment, and macOS throws an explicit
`PlatformNotSupportedException` because the pinned release has no public arbitrary
AppKit embedding API. Other platforms need runtime verification. No reflection into
Uno internals, rendered-image substitute, or separate-window fallback is used.

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
