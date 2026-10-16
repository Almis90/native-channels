# Currency Symbols sample

A separate MAUI app consuming **only** the `Plugin.Maui.Currency` reference plugin through
the local NuGet feed. This example verifies reusable plugin packaging; it is optional for applications
using NativeChannels, and the plugin has not been published to NuGet.org. It has no project
references, native source references, explicit AAR imports, or explicit XCFramework imports.

Enter a currency code or tap USD, EUR, GBP, or JPY. The displayed symbol comes from Android
`java.util.Currency`, Apple `NSLocale`, or Windows `CurrencyFormatter`, using the device locale.
Try `ZZZ` to see an unknown-code error.

Follow the [plugin build instructions](../../plugins/Plugin.Maui.Currency/README.md#build-and-run-from-source)
to produce the local NuGet and restore this sample into a fresh cache. `eng/restore-currency-sample.sh`
cleans generated outputs and verifies that all NativeChannels/plugin archives match the local feed.

On first appearance, the app runs real native checks for concurrent lookups, normalization, unknown
codes, malformed codes, cancellation, and a successful call after cancellation. It displays the result,
writes `currency-result.txt` to its app data directory, and logs `CURRENCY_SAMPLE PASS android: …` or
`CURRENCY_SAMPLE PASS apple: …` / `CURRENCY_SAMPLE PASS windows: …`.
The verification scripts fail if the result is absent or reports failure.

## Windows (0.5 framework preview)

```powershell
./eng/verify-windows.ps1 -Currency
```

Run from the repository root with the pinned SDK and `maui-windows` workload. This builds the Windows
`Plugin.Maui.Currency` 1.1.0-alpha.1 development package, runs its Windows tests, restores this app from
the local feed, and executes the Release x64 app. The app still has only one direct package reference.
Use `-BuildOnly` for adapter tests and compilation without UI execution. See the
[Windows plugin guide](../../plugins/Plugin.Maui.Currency/README.md#windows-development-preview).

## Android and Apple

```bash
# Android arm64 emulator
bash eng/restore-currency-sample.sh -p:TargetFrameworks=net10.0-android -r android-arm64
bash eng/dotnet.sh build samples/Currency.Sample -c Release -f net10.0-android -r android-arm64 --no-restore
bash eng/verify-android-currency.sh EMULATOR_SERIAL Release android-arm64

# iOS arm64 simulator
bash eng/restore-currency-sample.sh -p:TargetFrameworks=net10.0-ios -r iossimulator-arm64
bash eng/dotnet.sh build samples/Currency.Sample -c Release -f net10.0-ios -r iossimulator-arm64 --no-restore
bash eng/verify-ios-currency.sh SIMULATOR_UDID Release
```

Use `android-x64` for an x86_64 emulator. See the [verification record](../../plugins/Plugin.Maui.Currency/VERIFICATION.md)
for executed versus compile-only targets. Packages remain local; these commands do not publish them.
