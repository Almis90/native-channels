# Avalonia PDF reference plugin

`PdfViewer` embeds a native view with its own channel session for each attachment.
Android uses Kotlin/PdfRenderer, iOS and macOS use Swift/PDFKit, Windows uses a
Win32 child window and Windows.Data.Pdf, and Linux offers separate C++ Qt PDF and
GTK/Poppler adapters. Browser uses a DOM canvas and locally packaged PDF.js.

Open a local, unencrypted PDF after `Connected`. `OpenAsync`, `GoToPageAsync`,
`SetZoomAsync` and `GetStateAsync` return the instance identity, page, count and
relative zoom. The browser uses `OpenBytesAsync` with a maximum of 8 MiB. Call
`await PdfBrowser.InitializeAsync()` before starting the browser app.

Linux requires x64 and Avalonia's X11 backend (including XWayland). Set
`PdfLinux.Backend` before creating views. Use one toolkit per process; the two
sample executables select Qt and GTK separately. Native toolkit work stays in
C++; the managed adapter only hosts the shared C ABI and pumps toolkit events.

Native views occupy their own platform surface: avoid Avalonia overlays or
transforms over them. `DisconnectAsync` tears down the view and its channel;
`Reconnect` creates a new identity and an empty document. The sample has two
independent viewers to demonstrate ownership and reconnection.

See [the sample matrix](../../samples/Avalonia/README.md) for build commands and
implementation versus runtime-verification status. No new runtime verification
is claimed for these targets.

Browser assets are restored with `npm ci` in this directory. The pinned PDF.js
version and its Apache-2.0 license are included in the package; no CDN is required
at runtime. Linux links to system Qt/Poppler libraries; their distribution and
licensing requirements apply when shipping an application.
