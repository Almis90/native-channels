# Currency in Avalonia native macOS

Part of the [template-generated Avalonia solution](../README.md). The same Desktop project
also hosts [Windows](Windows.md), selected automatically on Windows or with
`-p:CurrencyDesktopTarget=windows`.
On Linux it selects the [Linux/X11 head and ICU adapter](Linux.md).

This package-only desktop application targets `net10.0-macos` with Avalonia 12.1.4 and AppKit.
It uses the shared Apple transport and Swift Currency adapter through `Plugin.Currency`.
It has no MAUI dependency or library project references. Native macOS is a separate target from
the existing UIKit/Mac Catalyst host.

## Registration and lifetime

`AppBuilder.UseNativeChannels(NativeCurrency.ConfigureServices)` registers the reusable
`NativeChannels.Avalonia` host. It owns the service provider for each view session and awaits
remaining sessions during explicit application shutdown.

`Program` initializes AppKit and starts Avalonia's classic desktop lifetime. `App` creates a window
containing the same `MainView` and `CurrencySession` used by the Android and iOS samples.
The shared view follows the MAUI sample's page layout, colors and help text, including
USD/EUR/GBP/JPY shortcut buttons and Enter/Go submission from the currency field.
The platform head’s `NativeCurrency.ConfigureServices` callback registers `AddNativeChannels(options => options.AddCurrency())` and `AddCurrency()`.
The Currency package brings its Swift framework and the shared channel packages into the app bundle.

Window close and application quit both cancel pending work and await asynchronous session disposal
before completing shutdown. Verification mode (`--verify`) runs the shared Currency checks and exits
after disposal. Interactive mode stays open for currency lookups.

The checks cover USD/EUR/GBP/JPY, normalization, structured native errors, invalid input, concurrent
calls, cancellation, and recovery. This is the Currency method-call proof for milestone 0.2.
The bridge has an AppKit `NSView` binding, but Avalonia native-view hosting, events, basic messages,
incoming UI callbacks, and the other reference plugins still need separate integration and checks.

## Build and run

Use a Mac with the [pinned .NET SDK and Xcode](../../../../docs/building.md). The deployment minimum is
macOS 14.0. Builds require the **macos** workload. Start with an empty `artifacts/packages` feed;
preserve any earlier feed before switching development targets.

```bash
bash eng/dotnet.sh workload install macos --version 10.0.401
python3 eng/build-apple.py --macos-only
bash eng/pack-apple.sh macos
bash eng/pack-currency-ios.sh macos
bash eng/pack-avalonia.sh
bash eng/restore-currency-avalonia.sh macos -r osx-arm64
bash eng/dotnet.sh build samples/Avalonia/Currency/Currency.Desktop -c Release -r osx-arm64 --no-restore
python3 eng/verify-currency-macos.py --runtime osx-arm64
```

On an Intel Mac, replace `osx-arm64` with `osx-x64`. The runner requires the execution architecture
to match the machine. To launch the window interactively:

```bash
open "samples/Avalonia/Currency/Currency.Desktop/bin/Release/net10.0-macos/osx-arm64/Currency Symbols — Avalonia.app"
```

`--macos-only` builds universal arm64/x64 Swift frameworks whose XCFramework platform is `macos`.
`NativeChannelsAppleTarget=macos` selects the managed development target; default mobile packaging
is unchanged. The scoped feed contains Core, NativeChannels, the Apple binding, and Currency.
The existing `pack-currency-ios.sh` also accepts `macos` to reuse its Apple-adapter packaging steps.
These development packages are not the published preview or the final combined milestone release.

## Verification

[Verify Avalonia native macOS](../../../../.github/workflows/shared-hosting-macos.yml) runs independently
on relevant pull requests and pushes, and supports manual dispatch. It builds Release consumers
for x64 and ARM64 from fresh caches and executes ARM64 on the Mac runner. x64 compilation alone
does not establish Intel runtime support. This job is separate from the four-platform MAUI
**Verify framework** and release-distribution workflows.

The runner checks the restored target, absence of MAUI dependencies, package-only native libraries and the shared application project, local
package sources and SHA-256 hashes. It inspects the application, bridge and Currency Mach-O files
for the native macOS platform and rejects UIKit/Catalyst dependencies. Success requires complete
Currency results, orderly disposal and exit code zero within 90 seconds.

Logs and `verification.json` are retained under `artifacts/shared-hosting-macos/<runtime>` and in
the `shared-hosting-macos` CI artifact. Evidence records the source revision and whether local
source changes were present. See the [verification record](../../../../docs/verification.md#avalonia-on-native-macos)
for executed configurations. Signing for distribution, notarization, minimum-OS execution, and full
milestone 0.2 acceptance remain separate checks.
