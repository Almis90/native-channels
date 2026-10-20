# Plugin.Uno.PdfViewer (experimental)

`new PdfViewer()` now uses Windows.Data.Pdf inside an Uno-hosted Win32 child
window on Windows 10 1809 or later. It reuses the document/rendering source from
the existing Windows PDF implementations, with Uno dispatch and lifecycle.
No MAUI or Avalonia package is required.

```csharp
var pdf = new PdfViewer { Height = 420 };
surface.Content = pdf;
// After Connected, on the owning UI thread:
await pdf.OpenAsync(absoluteLocalPath);
await pdf.GoToPageAsync(2);
await pdf.SetZoomAsync(2);
```

The viewer fits the page and supports 1x–4x zoom with dragging/wheel panning.
Rendering uses the shared maximum page dimension of 1600 pixels; this is not a
high-resolution print renderer. Invalid opens/pages preserve the previous
state. Concurrent operations return `pdf_busy`. Await `DisposeAsync` before
closing the window. Reconnect creates a fresh identity and channel; disconnected
channels and late results are rejected.

The custom `PdfViewer(Func<string, IUnoNativeView>)` constructor remains available.
`CreateNativeView(instanceId)` exposes the default factory for composition and
testing. Other platforms require a custom adapter and fail explicitly otherwise.
`OpenBytesAsync` remains browser-only. StateChanged reports command results and
UI-dispatched adapter callbacks; the Windows pointer gestures pan the page locally.

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
page/zoom pixel changes, invalid documents/pages, reconnect, stale channels and
shutdown with a pending request. Capturing the native paint surface does not prove
compositor visibility, physical input or every document's visual fidelity.
macOS remains blocked by Uno's public embedding API; Linux, Android, iOS and
browser adapters are still pending. The separate managed protocol fixture is
not counted as native rendering evidence.
