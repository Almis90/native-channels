# Plugins with Uno

The Uno work reuses `Plugin.Currency`, `Plugin.AudioStreamer`, and
`Plugin.VolumeMonitor`. There are no Uno-specific duplicates of those packages.
Their portable desktop builds register Windows and Linux factories and retain
their Apple module class names for the plain-.NET macOS bridge.

```csharp
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Currency;
using Plugin.AudioStreamer;
using Plugin.VolumeMonitor;

var host = app.UseNativeChannels(services =>
{
    services.AddNativeChannels(options =>
        options.AddCurrency().AddAudioStreamer().AddVolumeMonitor());
    services.AddCurrency().AddAudioStreamer().AddVolumeMonitor();
});
var session = host.CreateSession(window);
var currency = session.Services.GetRequiredService<ICurrencyService>();
var symbol = await currency.GetSymbolAsync("EUR", session.Stopping);
```

Use `session.Stopping` for volume enumeration and audio work. An audio playback
session belongs to the window session's registered `IAudioStreamer`; disposing
the session stops playback and releases native resources. The application must
also stop or pause playback on its platform's background/suspension transition,
and explicitly resume when foreground playback is appropriate. Merely switching
desktop window focus is not treated as a request to pause. Automated background
event integration on mobile and browser autoplay restrictions remain unverified.

## Desktop packaging

`NativeChannelsDesktopOnly=true` builds the generic `net10.0` plugin asset.
`NativeChannelsLinuxOnly=true` still builds Linux native assets and now includes
the same runtime-selectable managed factories. Windows audio and volume reuse
their existing WinMM and CoreAudio adapters. Portable Currency queries Windows
NLS for a matching ISO currency and its native symbol; the Windows-targeted
package retains its WinRT formatter. Symbols can therefore differ between these
backends and locales. Neither path ships a copied currency table.

For macOS, `eng/build-apple-desktop.py --arch arm64 --plugins` (and `--arch x86_64`)
builds the three existing Swift module implementations as dylibs. Pack each
plugin with `NativeChannelsIncludeAppleDesktopAssets=true` to include the RID
assets. The test-only probe asset package now contains just the native contract
sample; Currency belongs to its real plugin package. The expanded Mac build and
plugin packages have not been executed for this step.

## PDF control

The experimental [Plugin.Uno.PdfViewer](../plugins/Plugin.Uno.PdfViewer/README.md)
contains the Uno control, PDF state validation, UI-dispatched state callbacks,
and reconnect/disposal behavior. Its default constructor now uses a bundled
Windows.Data.Pdf renderer inside an Uno-hosted Win32 child window. The Windows
sample tests real page pixels, page/zoom changes, errors and lifecycle. Custom
`IUnoNativeView` factories remain supported. macOS lacks the required public
embedding API.
On iOS with Skia, the default factory uses the existing PDFKit UIKit backend.
The simulator sample runs MAUI's unchanged PDF assertions against two viewers;
no MAUI package, Uno patch or consumer workaround is required.
The Android factory similarly reuses the shared Android PdfRenderer adapter,
packaged as an AAR with `NativeChannelsAndroidOnly=true`.
The Linux X11 factory reuses the existing Qt/GTK native modules with Uno's public
`X11NativeWindow` wrapper and UI dispatcher. Pack with `NativeChannelsLinuxOnly=true`;
select `PdfLinux.Backend` before creating viewers (Qt by default, or Gtk).
The browser factory reuses the existing PDF.js DOM modules inside a public Uno
`BrowserHtmlElement` container. Initialize `PdfBrowser` before creating viewers
and use `OpenBytesAsync`. Browser-only shared messaging packages now include both
`net10.0-browser` and `net10.0-browserwasm` assets with the same implementation;
consumers need no framework fallback workaround.
No MAUI/Avalonia runtime dependency or Uno source patch is required.

Run the current native PDF check with:

```powershell
python eng/test-samples.py --framework uno --platform windows --sample PdfViewer --dotnet .tools/dotnet/dotnet.exe
```

It audits private Windows projection DLLs against the package and captures native
painting. Physical input and broader visual/document review remain outstanding.

## Executed acceptance

The five separate [Uno samples](../samples/Uno/README.md) now provide interactive
Currency, AudioStreamer and VolumeMonitor apps, an Interop diagnostic app, and
a native Windows PDF app with a separate protocol-fixture check. Run `eng/verify-uno-samples.ps1` after creating the local
preview packages. The remaining platform runs and broader visual/input acceptance are still outstanding.
The earlier feasibility probe below continues to use the managed PDF fixture.

On Windows, run:

```powershell
pwsh -File eng/verify-uno-feasibility.ps1 -PluginProbe
```

This packs the host, transport, three shared plugins and Uno PDF control, then
runs a package-only Uno consumer. The probe validates EUR/USD lookup and an
unknown currency error, an actual output-volume subscription, silent PCM,
pause/resume, and disposal while playback and the subscription are active. It
also validates the PDF command/state protocol, background-thread callbacks,
identity checks, reconnect and cleanup using a managed fixture.

The Windows run passed with a reused dependency cache; evidence is retained in
`artifacts/uno-feasibility/runs/ad58f7cdc9384d24b32b03a87490f8d1/`.
No system volume is changed and no audible test tone is played. Missing audio
hardware is a failed runtime test, not a skipped success. This result does not
establish macOS, Linux, mobile or browser plugin acceptance, or native PDF support.
