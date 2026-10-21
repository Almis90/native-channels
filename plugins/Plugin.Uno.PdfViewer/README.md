# Plugin.Uno.PdfViewer (experimental)

`new PdfViewer()` now uses Windows.Data.Pdf inside an Uno-hosted Win32 child
window on Windows 10 1809 or later. It reuses the document/rendering source from
the existing Windows PDF implementations, with Uno dispatch and lifecycle.
On iOS with Uno's Skia renderer it reuses the existing PDFKit UIKit backend.
No MAUI or Avalonia package is required.

```csharp
var pdf = new PdfViewer { Height = 420 };
surface.Content = pdf;
// After Connected, on the owning UI thread:
await pdf.OpenAsync(absoluteLocalPath);
await pdf.GoToPageAsync(2);
await pdf.SetZoomAsync(2);
```

The viewer fits the page and supports 1xâ€“4x zoom with dragging/wheel panning.
The Windows adapter handles hit testing, drag focus and pointer capture internally;
consumers do not need input-routing workarounds. Shift+wheel pans horizontally.
Rendering uses the shared maximum page dimension of 1600 pixels; this is not a
high-resolution print renderer. Invalid opens/pages preserve the previous
state. Concurrent operations return `pdf_busy`. Await `DisposeAsync` before
closing the window. Reconnect creates a fresh identity and channel; disconnected
channels and late results are rejected.

The custom `PdfViewer(Func<string, IUnoNativeView>)` constructor remains available.
`CreateNativeView(instanceId)` exposes the default factory for composition and
testing. Platforms other than Windows and iOS require a custom adapter and fail
explicitly otherwise.
`OpenBytesAsync` remains browser-only. StateChanged reports command results and
UI-dispatched adapter callbacks; the Windows pointer gestures pan the page locally.

## Windows accessibility

The native surface exposes a UI Automation pane with its current page count and
zoom, keyboard-focus properties, bounds and focus lookup. It raises focus and
name-change events and disconnects its provider on disposal. No consumer setup
is required. A real COM UIA client checks page identity, role and focus states in
the Windows sample. Screen-reader acceptance and the inspection tool's stale
global-focus report remain unresolved.

This is a raster viewer: Windows.Data.Pdf does not supply page text. The provider
does not advertise a document TextPattern or claim readable document content.
Text extraction and OCR are outside the current sample scope, matching the
MAUI and Avalonia plugin surfaces.

## Private Windows runtime dependencies

Uno defines its own Windows.* types. This package compiles against an aliased
Microsoft SDK projection and includes its runtime DLLs under buildTransitive/windows.
The package's MSBuild import references them, with aliases, only for Windows
net10.0/net10.0-desktop consumers so their identities appear in the app's deps.json.
The package audit checks that output DLLs exactly match the package bytes.

The unmodified Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll come from
Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 and remain subject to the
[Windows SDK license](https://aka.ms/WinSDKLicenseURL). NativeChannels source
retains its MIT license.

## Verification limits

The Windows sample captures real HWND painting through WM_PRINTCLIENT and checks
page/zoom and drag/wheel pixel changes, native hit testing, focus/capture,
cancelled drags, invalid documents/pages, reconnect, stale channels and
shutdown with a pending request. Capturing the native paint surface does not prove
compositor visibility, physical input or every document's visual fidelity.
The iOS sample runs the existing MAUI PDF verifier against two real PDFKit views
on an ARM64 simulator: document loading, navigation, zoom, UI callbacks,
isolation, errors, detach/reconnect and pending-work cleanup. The iOS package
uses Skia UI assets and stretches its native content internally; no consumer
workaround is required. Build the shared native backend with
`python3 plugins/Plugin.Maui.PdfViewer/eng/build-apple.py` and pack with
`NativeChannelsAppleTarget=ios`; the iOS runner automates both steps.
Physical-device input and visual acceptance remain unverified.

macOS remains blocked by Uno's public embedding API; Linux, Android and
browser adapters are still pending. The separate managed protocol fixture is
not counted as native rendering evidence.
