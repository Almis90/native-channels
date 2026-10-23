# Plugin.Currency

A reference implementation for developers building reusable plugins with NativeChannels. It looks up currency symbols using Android's `java.util.Currency`, Apple's `NSLocale`, Windows' `CurrencyFormatter`, Linux's system ICU library, or the browser's `Intl.NumberFormat` and demonstrates a typed C# API, native module registration, and reusable packaging.

You do not need this plugin to use NativeChannels. Start with the framework's [getting-started guide](https://github.com/Almis90/native-channels/blob/main/docs/getting-started.md) to integrate your own native code.

```csharp
string symbol = await currency.GetSymbolAsync("EUR"); // €
```

The reference plugin is built locally and has **not been published to NuGet.org**.

## How the example works

1. [CurrencyService](src/Plugin.Currency/CurrencyService.cs) validates and normalizes the code, then calls `getSymbol` on the `plugin.maui.currency` MethodChannel.
2. The [Kotlin module](native/android/src/main/java/dev/nativechannels/currency/CurrencyModule.kt) and [Swift module](native/apple/Sources/CurrencyModule.swift) call the platform currency APIs and return a symbol or structured error through the generic bridge.
   The [Windows module](native/windows/WindowsCurrencyModule.cs) does the same through WinRT from C#.
3. [Registration extensions](src/Plugin.Currency/CurrencyExtensions.cs) configure the native modules and expose `ICurrencyService` through dependency injection.
4. The [package project](src/Plugin.Currency/Plugin.Currency.csproj) includes the managed API, Kotlin AAR, and Swift XCFramework for mobile builds. Its [transitive targets](src/Plugin.Currency/buildTransitive/Plugin.Currency.targets) add the native assets to the consuming application. The Windows development package embeds its adapter in the managed assembly and has no mobile native assets.
5. A separate [sample application](https://github.com/Almis90/native-channels/tree/main/samples/Maui/Currency) restores the plugin and its chosen `NativeChannels.Maui` host from packages. This verifies that consumers receive the native adapters without project references or manual native imports.

The plugin depends on `NativeChannels`, with no MAUI dependency or `UseMaui` build requirement.
Its existing package ID and channel name are retained. Applications select their UI host separately.

Use this structure as a starting point for your own plugin, with your own package ID, channel name, native module names, and public API. Keep a separate package-consuming app to catch missing distribution assets.

## Behavior

- Accepts a three-letter currency code. Whitespace is trimmed and ASCII letters are normalized to uppercase: `" usd "` becomes `"USD"`.
- Uses the device's current native display locale. USD can return `$`, `US$`, or another localized representation. Results can differ between operating systems and OS versions.
- A valid currency can return its code when the platform has no localized symbol. This plugin does not maintain its own currency database.
- Null input throws `ArgumentNullException`; malformed input throws `ArgumentException`.
- A well-formed code unknown to the platform, such as `ZZZ`, throws `NativeChannels.ChannelException` with code `invalid_currency_code`, the input code in `Details`, and native platform identity.
- Cancellation uses the standard .NET cancellation token. The plugin inherits the NativeChannels service timeout, including an explicitly configured infinite timeout.
- The DI container owns the singleton service and disposes its channel. Do not manually dispose a service resolved from DI.

This plugin looks up symbols; it does not convert amounts, fetch exchange rates, accept country codes, or format monetary values.

## Windows development preview

The Windows adapter targets `net10.0-windows10.0.19041.0` and uses the NativeChannels Windows transport.
Registration and the public `ICurrencyService` API are identical on every platform; `AddCurrency()`
selects a fresh Windows module automatically.

With the pinned .NET SDK and `maui-windows` workload installed, run from the repository root:

```powershell
./eng/verify-windows.ps1 -Currency
```

This builds the Windows framework packages and `Plugin.Currency` **1.1.0-alpha.1**, tests the
packaged adapter against real Windows APIs, and publishes/runs the separate Currency sample. Add
`-BuildOnly` to run the adapter tests and compile the app without launching its UI. The script uses
isolated caches and verifies that the app restores only packages whose hashes match the local feed.
Results are saved under `artifacts/windows/verification/<run>/currency-result.txt` and packages under
`artifacts/windows/packages`. Source builds now use `1.1.0-alpha.1` on every platform. The native CI
workflow combines mobile and Windows outputs from the same commit before its consumer checks.
Use `-PackageFeed artifacts/packages` to verify a downloaded combined feed without rebuilding it;
see [combined packaging and release acceptance](../../docs/windows-integration.md#combined-platform-packages).
Nothing is published by these commands.

Windows validation queries the OS's
[CurrencyIdentifiers](https://learn.microsoft.com/en-us/uwp/api/windows.globalization.currencyidentifiers)
properties. This is necessary because the formatter also accepts unknown three-letter codes.
[CurrencyFormatter](https://learn.microsoft.com/en-us/uwp/api/windows.globalization.numberformatting.currencyformatter)
supplies the symbol or currency-code fallback using the user's language and region. Since WinRT exposes
formatted amounts rather than a symbol getter, the adapter formats a single Latin zero without decimal
or grouping separators, removes the digit, and trims surrounding spacing and directional markers.
No currency/symbol database is bundled. Supported identifiers can differ from Android and Apple.
Symbols can also vary with Windows locale data: for `ja-JP` / `JP`, yen uses `¥` on the verified
Windows 10 22H2 and LTSC 2019 VMs and `￥` on the verified Windows 11 host. The adapter preserves the
OS-provided form.

## Build and run from source

For the Linux development feed, see the [Avalonia Linux proof](../../samples/Avalonia/Currency/Currency.Desktop/Linux.md).
For packaged JavaScript adapters and WebAssembly, see the [Avalonia Browser proof](../../samples/Avalonia/Currency/Currency.Browser/README.md).
It packages a C++ channel module that links ICU directly, without mobile assets or MAUI.
The reusable [Linux bridge](../../native/linux/README.md) uses a language-neutral C ABI;
no currency-specific C# native bindings are required. Build with `g++`, `pkg-config`, and `libicu-dev`.
The Linux archives and Ubuntu x64 runtime checks are separate from the mobile build below.

From the repository root, with the pinned SDK/workloads, Xcode, JDK, and Android SDK installed:

```bash
# Build and package NativeChannels first.
bash eng/build-android.sh
python3 eng/build-apple.py
bash eng/pack.sh

# Build the plugin adapters and package.
native/android/gradlew -p native/android --no-daemon :currency:assembleRelease :currency:testReleaseUnitTest
python3 plugins/Plugin.Currency/eng/build-apple.py
bash plugins/Plugin.Currency/eng/pack.sh
python3 plugins/Plugin.Currency/eng/inspect-package.py

# Restore the separate application into a fresh package cache.
bash eng/restore-currency-sample.sh -p:TargetFrameworks=net10.0-ios -r iossimulator-arm64
bash eng/dotnet.sh build samples/Maui/Currency -c Release -f net10.0-ios -r iossimulator-arm64 --no-restore
bash eng/verify-ios-currency.sh SIMULATOR_UDID Release
```

For Android, restore with `-p:TargetFrameworks=net10.0-android -r android-arm64`, build the same target/RID, then run `bash eng/verify-android-currency.sh emulator-5580 Release android-arm64`. Use `android-x64` for an x86_64 emulator. The Android SDK and JDK environment variables must be configured as described in the repository's [development guide](https://github.com/Almis90/native-channels/blob/main/docs/building.md).

The [sample](https://github.com/Almis90/native-channels/tree/main/samples/Maui/Currency) has an editable currency code and quick lookup buttons. Its startup verification exercises real native lookups, normalization, concurrent requests, native errors, and cancellation. The app references this plugin and `NativeChannels.Maui`; the platform transport and native adapters arrive transitively.

Portable facade tests run with `bash eng/dotnet.sh test`. They compile the actual facade source against the portable Core project. Native tests exercise the platform currency APIs separately:

```bash
xcrun swiftc -swift-version 5 \
  plugins/Plugin.Currency/native/apple/Sources/CurrencySymbols.swift \
  plugins/Plugin.Currency/native/apple/Tests/main.swift \
  -o artifacts/currency-swift-tests
artifacts/currency-swift-tests
```

See [verification status](https://github.com/Almis90/native-channels/blob/main/plugins/Plugin.Currency/VERIFICATION.md) for the distinction between implemented, compiled, executed, and unverified platforms. No package publication is performed by these scripts.

## Consume the local example package

After following the build instructions above, an application configured with the local feed can reference the example:

```xml
<PackageReference Include="Plugin.Currency" Version="1.1.0-alpha.1" />
<!-- Select the MAUI host explicitly for a MAUI application. -->
<PackageReference Include="NativeChannels.Maui" Version="0.1.0-preview.1" />
```

Register the native module and service in `MauiProgram.cs`:

```csharp
using NativeChannels;
using Plugin.Currency;

builder.UseNativeChannels(options => options.AddCurrency());
builder.Services.AddCurrency();
```

If your app already calls `UseNativeChannels`, add `options.AddCurrency()` to that existing configuration. Call `UseNativeChannels` once per app. Other plugins can register their modules in the same configuration.

A platform application that supplies its own lifecycle can instead use
`services.AddNativeChannels(options => options.AddCurrency())` and `services.AddCurrency()`.
See [shared registration](../../docs/configuration.md#registration-without-a-maui-host) for ownership
and package references. The [Avalonia Android sample](../../samples/Avalonia/Currency/Currency.Android/README.md)
uses the same package through shared registration, with dedicated CI checking both hosts together.

Inject `ICurrencyService` into a page or view model:

```csharp
public sealed class PriceViewModel(ICurrencyService currency)
{
    public Task<string> GetSymbolAsync(CancellationToken cancellationToken = default)
        => currency.GetSymbolAsync("USD", cancellationToken);
}
```

## Native macOS development target

The [Avalonia macOS sample](../../samples/Avalonia/Currency/Currency.Desktop/README.md) consumes the Currency
API through `net10.0-macos`, AppKit and a macOS build of the same Swift adapter. Select
`NativeChannelsAppleTarget=macos` when packing; the guide includes native framework build and
package verification steps. This scoped development feed is separate from the published mobile
preview and does not add macOS to the default combined release feed.

## Package layout

- `lib/net10.0-*`: the C# service and registration extensions.
- `native/android/currency.aar`: the Kotlin module, with consumer preservation rules.
- `native/apple/MauiCurrencyNative.xcframework`: iOS device, simulator, and Mac Catalyst slices.
- `buildTransitive/Plugin.Currency.targets`: imports the correct native adapter for the consuming platform.

The module uses the `plugin.maui.currency` MethodChannel and its `getSymbol` method. The request argument and result are strings. Only the existing generic NativeChannels bridge is bound to C#; the currency module needs no method-specific binding.

## License

MIT, as specified by the repository [LICENSE](https://github.com/Almis90/native-channels/blob/main/LICENSE).
