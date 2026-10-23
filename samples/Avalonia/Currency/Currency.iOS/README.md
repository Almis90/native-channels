# Currency in Avalonia iOS

Part of the [template-generated Avalonia solution](../README.md).

This package-only .NET 10 iOS application consumes the same `Plugin.Currency` archive as the
[MAUI sample](../../../Maui/Currency/README.md). It pins Avalonia 12.1.4 and has no MAUI dependency or
library project references. The shared `Currency` application project contains the view and session used by the
[Avalonia Android sample](../Currency.Android/README.md).

## Registration and lifetime

`AppBuilder.UseNativeChannels(NativeCurrency.ConfigureServices)` registers the reusable
`NativeChannels.Avalonia` host. It owns the service provider for each view session and awaits
remaining sessions during explicit application shutdown.

`AppDelegate` uses `AvaloniaAppDelegate<App>`. `App` assigns a `MainView` to the iOS
`ISingleViewApplicationLifetime`. The view owns a `CurrencySession`. The platform head’s `NativeCurrency.ConfigureServices` callback registers
`AddNativeChannels(options => options.AddCurrency())` and `AddCurrency()` in a service provider.
The Currency package supplies the native Apple adapter and the shared channel dependencies.

The sample uses one application view/session and disables multiple scenes. It retains the session
through background/foreground transitions. A termination callback starts asynchronous cancellation
and disposal without blocking the UI thread; iOS can terminate a suspended process without calling
managed cleanup, so shutdown completion is best effort. General scene ownership, suspension, and
event-stream lifecycle acceptance remain part of the [Avalonia milestone](../../../../docs/roadmap.md).

The shared verification scenarios run when the view loads: USD/EUR/GBP/JPY, normalization,
structured native errors, invalid input, concurrency, cancellation, and recovery. The app logs
`CURRENCY_AVALONIA PASS apple: …` and atomically saves the result in
`Documents/currency-avalonia-result.txt`. Native UI and other channel contracts are outside this
Currency method-call proof.

## Verify the same package in both hosts

**Shared hosting / iOS Currency** is a reusable workflow called whenever **Verify framework**
selects iOS. It reuses the same run's framework feed and native bridge, builds/tests the Swift
Currency adapter, and packs Currency once for iOS. The development package has no Android AAR.
Both hosts restore from that feed into separate fresh caches. The verifier compares all four shared
archive hashes, rejects library project references, and rejects MAUI dependencies in Avalonia.

Both Release apps compile for `iossimulator-arm64` and execute sequentially on the same simulator.
The runner removes old result files, launches each app, waits for complete results, and requires
matching native output. A failure in either host fails framework verification. The job retains
package hashes, source revision, runtime records, logs, and screenshots in `shared-hosting-ios`.
It executes even at framework **Compile** depth; **Runtime** also runs the full framework sample.

Run **Verify framework** with platforms **iOS** or **All** to request the proof. On a Mac configured
with the [pinned Xcode, SDK and iOS workload](../../../../docs/building.md), use a clean development feed:

```bash
python3 eng/build-apple.py
bash eng/pack-apple.sh ios
bash eng/pack-currency-ios.sh
bash eng/pack-avalonia.sh
bash eng/restore-currency-sample.sh -p:TargetFrameworks=net10.0-ios -r iossimulator-arm64
bash eng/restore-currency-avalonia.sh ios -r iossimulator-arm64
python3 eng/verify-shared-hosting.py packages --platform ios
bash eng/dotnet.sh build samples/Maui/Currency -c Release -f net10.0-ios -r iossimulator-arm64 --no-restore
bash eng/dotnet.sh build samples/Avalonia/Currency/Currency.iOS -c Release -r iossimulator-arm64 --no-restore
bash eng/verify-shared-hosting-ios.sh SIMULATOR_UDID
```

Use the identifier of a booted iOS simulator. Simulator results do not establish physical-device
support, signing, or App Store distribution. These are development packages; final combined-package
release acceptance is separate. See the [iOS verification record](../../../../docs/verification.md#shared-hosting-on-ios) for
executed evidence. This sample does not complete milestone 0.2.

## Run the same app on Mac Catalyst

Avalonia.iOS 12.1.4 includes a Mac Catalyst assembly. The same application, delegate, shared view,
and session can run on Catalyst by setting `CurrencyAppleTarget=maccatalyst`. Use a fresh development feed
for this target; preserve an existing `artifacts/packages` feed before switching platforms.

```bash
python3 eng/build-apple.py
bash eng/pack-apple.sh maccatalyst
bash eng/pack-currency-ios.sh maccatalyst
bash eng/pack-avalonia.sh
bash eng/restore-currency-avalonia.sh catalyst -r maccatalyst-arm64
bash eng/dotnet.sh build samples/Avalonia/Currency/Currency.iOS -c Release \
  -p:CurrencyAppleTarget=maccatalyst -r maccatalyst-arm64 --no-restore
open "samples/Avalonia/Currency/Currency.iOS/bin/Release/net10.0-maccatalyst/maccatalyst-arm64/Currency Symbols — Avalonia Catalyst.app"
```

This uses the UIKit/Catalyst backend. The [native macOS sample](../Currency.Desktop/README.md)
uses AppKit and the same shared view. See the [Catalyst verification record](../../../../docs/verification.md#avalonia-on-mac-catalyst)
for the local ARM64 test results and remaining coverage.
