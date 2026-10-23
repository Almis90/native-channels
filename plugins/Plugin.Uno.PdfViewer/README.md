# Plugin.Uno.PdfViewer (experimental)

`new PdfViewer()` now uses Windows.Data.Pdf inside an Uno-hosted Win32 child
window on Windows 10 1809 or later. It reuses the document/rendering source from
the existing Windows PDF implementations, with Uno dispatch and lifecycle.
On iOS with Uno's Skia renderer it reuses the existing PDFKit UIKit backend;
on Android it reuses the existing Android PdfRenderer backend.
On Linux X11 it reuses the existing Qt/GTK native backends through Uno's public
`X11NativeWindow` wrapper. In the browser it reuses Avalonia's PDF.js modules
inside a public Uno `BrowserHtmlElement` container.
On macOS it reuses the existing AppKit PDFKit backend through the optional Uno
adapter's temporary native hosting bridge. No patched Uno build is required.
The bridge uses reflection pending [Uno #25639](https://github.com/unoplatform/uno/issues/25639).
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
testing. Platforms other than Windows, Linux X11, browser, iOS and Android require a custom adapter and fail
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

For Android, build `:pdf:assembleRelease` with `native/android/gradlew` and pack
with `NativeChannelsAndroidOnly=true`. The package includes the shared PDF AAR
and selects Uno's Skia UI assets with Android runtime types. The Android runner
automates native build, packaging and the shared PDF verifier.
The same PDF verifier passed on the API 36 x64 emulator; physical-device input
and visual acceptance remain unverified.

For Linux x64, pack with `NativeChannelsLinuxOnly=true`. This builds and includes
both existing native PDF libraries; install the Qt6 PDF/Widgets or GTK3/Poppler
system dependencies for the backend you select. `PdfLinux.Backend` defaults to
`LinuxPdfBackend.Qt`; set it to `LinuxPdfBackend.Gtk` before creating viewers to
use GTK. This is the same choice exposed by the Avalonia adapter. Both passed
the unchanged shared PDF verifier on Ubuntu 24.04 x64 X11/Xvfb; this does not
establish visual rendering or physical-input acceptance.

For browser builds, run `npm ci` in `plugins/Plugin.Avalonia.PdfViewer` to prepare
the shared PDF.js dependency, then pack with `NativeChannelsBrowserOnly=true`.
The package includes the existing PDF.js adapter, worker, fonts, CMaps, WASM
helpers and license. Call `await PdfBrowser.InitializeAsync()` before creating
viewers, then use `OpenBytesAsync` with a PDF up to 8 MiB. This initialization
pattern matches the Avalonia browser adapter. The managed browser asset targets
Uno's `net10.0-browserwasm` framework; no Avalonia package or Uno source patch is
required.

macOS remains blocked by Uno's public embedding API. The separate managed protocol fixture is
not counted as native rendering evidence.

For macOS, build `eng/build-apple-desktop.py --arch arm64 --sample --views`
(and `--arch x86_64` for x64 assets), then pack with `NativeChannelsDesktopOnly=true`,
`NativeChannelsMacOSOnly=true` and `NativeChannelsIncludeAppleDesktopAssets=true`.
`eng/verify-uno-macos-views.py` automates packaging and the same MAUI PDF verifier
against two real PDFKit views. ARM64 passes document/navigation/zoom, callbacks,
independence, errors, reconnect and pending-work cleanup on released Uno 6.7.135.
x64 libraries cross-compile; x64 execution, trimming/AOT and physical input remain unverified.

Desktop builds now share one managed DLL that selects Windows, Linux or macOS at
runtime. The platform packaging flags select the native payloads to include; they
no longer produce competing managed implementations at `lib/net10.0`.
