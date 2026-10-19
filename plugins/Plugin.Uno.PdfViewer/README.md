# Plugin.Uno.PdfViewer (experimental)

This is the Uno control and lifetime layer for the existing native PDF channel
protocol. It is **not a complete native PDF implementation**. Construct
`PdfViewer(Func<string, IUnoNativeView> create)` with an adapter factory using a
public Uno native-element wrapper supported by your backend. No built-in Uno PDF
adapter is currently validated or bundled. This prerequisite is explicit rather
than falling back to images or a separate native window.

The control offers `OpenAsync`, `OpenBytesAsync` (browser only), `GoToPageAsync`,
`SetZoomAsync`, and `GetStateAsync`, plus `Connected`, `Disconnected`, and
`StateChanged`. State replies must contain the matching `instanceId`, `page`,
`pageCount`, and `zoom`. Late replies from a disconnected adapter are rejected.
Call methods on the owning UI queue. Incoming state callbacks are dispatched to
that queue by the control. Await `DisposeAsync` before disposing its
channel session or closing the window.

`Reconnect` creates a fresh identity and channel. `DisconnectAsync` invalidates
the old channel and waits for cleanup. A factory must release any resources it
allocates if its own construction fails.

The current Uno host rejects macOS embedding because Uno 6.7.135 exposes no public
arbitrary AppKit hosting API. Windows attachment is unresolved, and Linux,
Android, iOS, and browser PDF adapters have not been integrated or verified for
Uno. Existing MAUI/Avalonia adapter implementations are reuse candidates; those
packages are not dependencies of this control.
