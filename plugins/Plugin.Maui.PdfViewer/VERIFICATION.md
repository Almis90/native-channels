# PdfViewer verification

The plugin implements Android PdfRenderer, Apple PDFKit, and Windows.Data.Pdf adapters, with owned
native view instances and private method channels. The bundled sample document has three pages.

## Build verification

On 2026-10-15 the Android adapter and Apple XCFramework compiled, including iOS device, simulator,
and Mac Catalyst slices. This is native compilation, not application runtime verification. The
current preview still requires fresh package-only consumer acceptance on each target. See the
[platform verification record](../../docs/verification.md).

## Consumer checks

The sample verifies document loading, page navigation, zoom, callbacks, independent instances,
structured errors, and detach/reconnect. A native-button fixture in the framework sample separately
exercises generic view lifecycle; it is not the PDF example.

## Remaining checks

Execute current-preview packages on Android and iOS, including manual gestures, then verify physical
devices and Catalyst independently. Windows acceptance includes native API tests, view lifecycle,
multiple instances, and installed-app loading; see [Windows plugin verification](../../docs/windows-plugins-verification.md).
Compilation of a native slice does not establish application loading or rendering.
