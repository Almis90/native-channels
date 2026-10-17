# Currency in Avalonia

This solution was generated with the official `Avalonia.Templates` 12.1.4 cross-platform
template, then populated with the existing Currency sample:

```bash
bash eng/dotnet.sh new install Avalonia.Templates::12.1.4
bash eng/dotnet.sh new avalonia.xplat -n Currency -o samples/Avalonia/Currency -av 12.1.4 -rvl true
```

These commands reproduce the template at its current location; do not regenerate over the working sample.
Open `Currency.slnx` to work on the whole Avalonia application.

```text
Avalonia/Currency/
  Currency.slnx
  Directory.Packages.props
  Currency/             shared App, Views, assets and session contract (net10.0)
  Currency.Android/     Android startup and native adapter registration
  Currency.iOS/         iOS startup; also supports the Catalyst target
  Currency.Desktop/     native macOS/AppKit, Windows/Win32 and Linux/X11 startup
  Currency.Browser/     WebAssembly startup and packaged JavaScript/Intl adapter
```

The platform projects reference the shared `Currency` application project normally, as in
the template. Android, iOS, native macOS, Windows, Linux, Browser and Catalyst use the same view and behavior. The
existing C# view layout is preserved in `Currency/Views/MainView.axaml.cs`; its XAML file
is the template's view entry point. Native registration stays in each platform head's
`NativeCurrency.ConfigureServices` method, registered through `AppBuilder.UseNativeChannels`.
The shared UI consumes the `NativeChannels.Avalonia` package, creates an owned session per view,
and awaits the host during application shutdown. See the [host guide](../../../docs/avalonia-host.md).

The deliberate changes from an empty template are the Currency UI, native plugin registration,
central package versions, and `Currency.Desktop` selecting `net10.0-macos` for AppKit or
`net10.0-windows10.0.19041.0` for the Windows adapter. The target follows the host OS and can be
selected explicitly with `CurrencyDesktopTarget=macos`, `CurrencyDesktopTarget=windows`, or
`CurrencyDesktopTarget=linux`. Linux uses `net10.0` with the scoped Linux transport and ICU adapter packages.
`Currency.Browser` uses `net10.0-browser` with scoped browser packages and the browser's Intl API.

All NativeChannels and Currency libraries still come from NuGet packages. Restore checks
allow only the shared application project reference and reject references to library projects.
Build individual platform projects against their corresponding local development package feed:

- [Android build and verification](Currency.Android/README.md)
- [iOS and Mac Catalyst build and verification](Currency.iOS/README.md)
- [Native macOS build and verification](Currency.Desktop/README.md)
- [Windows build and verification](Currency.Desktop/Windows.md)
- [Linux build and verification](Currency.Desktop/Linux.md)
- [Browser build and verification](Currency.Browser/README.md)

The separate [MAUI sample](../../Currency.Sample/README.md) remains in its existing location.
