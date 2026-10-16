# PDF Viewer sample

An offline showcase of `PlatformChannelView`: a real native PDF document, controlled through
per-instance method channels. Uses `Plugin.Maui.PdfViewer` from the repository's local package feed,
with no project references or direct native references in the consumer.

On startup the app verifies document loading, page and zoom commands, native callbacks, two-view
isolation, error handling and detach/reconnect. It then opens the included guide for interactive use.
Use Previous/Next, pinch or zoom buttons, and Reopen to create a fresh native instance. On Android,
swipe to turn pages while at fit-to-page zoom. Android raster rendering has no text selection or
screen-reader document text; the navigation controls remain accessible.

The original three-page document is MIT-licensed with this repository. Regenerate it using
`python3 samples/PdfViewer.Sample/Resources/create-document.py` (requires `reportlab`).

Build/run commands and platform limits are in the [plugin guide](../../plugins/Plugin.Maui.PdfViewer/README.md).
Runtime checks write `PDF_SAMPLE PASS ...` and `pdf-result.txt` in app data. Execution status lives
in the [verification record](../../plugins/Plugin.Maui.PdfViewer/VERIFICATION.md).
