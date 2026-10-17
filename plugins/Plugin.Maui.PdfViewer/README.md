# Plugin.Maui.PdfViewer

A reference plugin for embedding native PDF views with NativeChannels. Swift hosts PDFKit's `PDFView`;
Kotlin renders pages with Android's `PdfRenderer` in a native gesture surface. Windows renders with
`Windows.Data.Pdf` inside a WinUI image and zoomable scroll viewer. C# sends commands and
receives native state changes over each `PlatformChannelView` instance's method channel.

This is a source example, not a published NuGet product. Build the library and this adapter locally,
then reference the resulting local package from your app. No individual PDF methods need C# bindings.

## Use the example

Register `options.AddPdfViewer()` inside `builder.UseNativeChannels(...)`. Add a
`Plugin.Maui.PdfViewer.PdfViewer` to your layout before invoking document operations:

```csharp
var viewer = new Plugin.Maui.PdfViewer.PdfViewer();
viewer.Connected += async (_, _) => await viewer.OpenAsync(localPdfPath);
viewer.StateChanged += (_, state) =>
    pageLabel.Text = $"Page {state.Page} of {state.PageCount}";

await viewer.GoToPageAsync(2); // One-based page number.
await viewer.SetZoomAsync(2);  // Twice the fit-to-page scale.
```

Run calls on the UI thread after `Connected`. Copy a bundled `MauiAsset` to app storage before
opening it; the native implementations receive an absolute local path. Cancellation ends the
caller's wait, but may not interrupt a native parse/render already in progress.

`StateChanged` is a native callback, including gestures. Commands return a `PdfState` with the
instance ID, page, page count and zoom. A disconnected/recreated view has no document: reopen it
in `Connected`. The old channel, callback registration and document resources are released.
Other viewers keep their own state. `DisconnectAsync` releases native content explicitly;
`Reconnect` creates a new native instance. Document state is not restored automatically.

## Scope and limits

- Windows 10 build 17763+: one raster page, capped at 1600 pixels on either edge. Use the shared
  page-navigation API and the sample's Previous/Next buttons. WinUI supports zoom and scrolling;
  resizing refits the page. Text selection, text accessibility, search and annotations are not
  implemented. Each view owns its document stream and ignores work cancelled during detach.

- Android API 24+: one rendered page at a time, capped at 1600 pixels on either edge. Pinch or
  double-tap to zoom, drag to pan, and swipe horizontally at fit-to-page to change pages. Raster
  pages do not provide text selection, text accessibility, search or annotations. Rendering and
  file cleanup run on one background worker per view. Displayed bitmaps are released for garbage
  collection rather than recycled while Android might still draw them.
- iOS and Mac Catalyst 15+: native PDFKit page navigation, gestures and rendering. Zoom is relative
  to fit-to-page; resizing refits the document. PDFKit features beyond the shared API remain
  platform-specific.
- Readable, unencrypted local PDFs only. No downloading, file picker, password UI, editing or remote
  URL loading. A failed open preserves the previous document. `pdf_open_failed`, `pdf_render_failed`,
  `invalid_page`, `invalid_zoom` and `pdf_busy` are structured native errors. Await document
  operations rather than submitting overlapping opens/navigation.
- This is a compact integration example, not a production document engine or untrusted-file sandbox.

## Build and verify

On Windows, run `./eng/verify-windows.ps1 -Plugin PdfViewer` from the repository root. It packs
the adapter, tests native PDF rendering, and runs the package-only sample's UI lifecycle checks.
Use `-BuildOnly` for compilation and native renderer tests without launching the sample.

First build and pack NativeChannels following [the build guide](../../docs/building.md), then run:

```sh
# Configure JAVA_HOME (JDK 21) and ANDROID_HOME as described in the build guide.
native/android/gradlew -p native/android --no-daemon :pdf:assembleRelease
python3 plugins/Plugin.Maui.PdfViewer/eng/build-apple.py
bash plugins/Plugin.Maui.PdfViewer/eng/pack.sh
python3 plugins/Plugin.Maui.PdfViewer/eng/inspect-package.py

bash eng/restore-pdf-sample.sh -p:TargetFrameworks=net10.0-android -r android-arm64
bash eng/dotnet.sh build samples/Maui/PdfViewer -c Release -f net10.0-android -r android-arm64 --no-restore
bash eng/verify-android-pdf.sh EMULATOR_SERIAL Release android-arm64
```

For iOS use `net10.0-ios` / `iossimulator-arm64`, then
`bash eng/verify-ios-pdf.sh SIMULATOR_UDID Release`. Run one simulator at a time.
The sample has only package references; restore uses a new cache and verifies archive hashes.
The plugin package carries the AAR, all XCFramework slices and transitive native imports.

See [the sample](../../samples/Maui/PdfViewer/README.md) and [verification status](VERIFICATION.md).

## Native adapters

- [Android](native/android/src/main/java/dev/nativechannels/pdf): PdfRenderer and gesture handling.
- [Apple](native/apple/Sources): PDFKit views and callbacks.
- [Windows view](native/windows/WindowsPdfView.cs) and [document](native/windows/WindowsPdfDocument.cs): WinUI hosting and Windows.Data.Pdf rendering.
