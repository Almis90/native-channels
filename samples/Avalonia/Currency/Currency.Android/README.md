# Currency in Avalonia Android

Part of the [template-generated Avalonia solution](../README.md).

This package-only sample looks up native currency symbols using the same `Plugin.Currency`
package as the [MAUI sample](../../../Maui/Currency/README.md). It targets .NET 10 Android and pins
Avalonia 12.1.4. It has no MAUI package dependency and references only the shared application project.
The view and session in `samples/Avalonia/Currency/Currency` are also used by the
[Avalonia iOS sample](../Currency.iOS/README.md).

## Registration and lifetime

`AppBuilder.UseNativeChannels(NativeCurrency.ConfigureServices)` registers the reusable
`NativeChannels.Avalonia` host. It owns the service provider for each view session and awaits
remaining sessions during explicit application shutdown.

The platform headâ€™s `NativeCurrency.ConfigureServices` callback calls `AddNativeChannels(options => options.AddCurrency())`
and `AddCurrency()`. It registers the portable `CurrencySession` facade; the Avalonia host owns disposal. Avalonia's Android activity view factory
creates a new view/session for each activity. `MainActivity.OnDestroy` cancels outstanding view work and
starts asynchronous provider disposal, allowing the native bridge to close without blocking Android's
UI thread. Android process termination does not promise a managed cleanup callback.

The view starts verification when loaded and displays native lookup results. Both applications compile
`samples/Currency.Shared/PluginVerification.cs`, which checks concurrent USD/EUR/GBP/JPY lookups,
normalization, native structured errors, invalid input, cancellation and recovery. The Avalonia app
logs `CURRENCY_AVALONIA PASS android: â€¦` only after those scenarios succeed.

## Verify the same package in both hosts

The reusable **Shared hosting / Android Currency** workflow runs whenever **Verify framework** selects
Android. It reuses that run's Android framework archives, builds the native Currency adapter, packs
Currency once, and restores the two consumers into separate fresh caches. SHA-256 checks prove that
both hosts use exactly the same shared package archives. The check rejects library project references and
any `NativeChannels.Maui` or `Microsoft.Maui.*` dependency in Avalonia's restored graph.

Both Release APKs execute in one Android x64 emulator. The verifier requires matching native results
from both processes. The `shared-hosting-android` artifact contains package hashes, each app's logs,
and a success record only after both hosts pass. This proof executes even when the framework sample's
depth is **Compile**; **Runtime** additionally executes the full framework sample.

To request it manually, run **Verify framework** with platforms **Android** (or **All**). To reproduce
locally on a configured Android development machine, start with a clean Android-only framework feed:

```bash
bash eng/build-android.sh
bash eng/pack-android.sh
bash eng/pack-currency-android.sh
bash eng/pack-avalonia.sh
bash eng/restore-currency-sample.sh -p:TargetFrameworks=net10.0-android -r android-x64
bash eng/restore-currency-avalonia.sh -r android-x64
python3 eng/verify-shared-hosting.py packages
bash eng/dotnet.sh build samples/Maui/Currency -c Release -f net10.0-android -r android-x64 --no-restore
bash eng/dotnet.sh build samples/Avalonia/Currency/Currency.Android -c Release -r android-x64 --no-restore
bash eng/verify-shared-hosting-android.sh emulator-5554
```

See [building](../../../../docs/building.md) for the pinned SDK, Android workload, JDK and Android SDK setup.
These are development archives, not combined release packages. No packages are published.

## Scope

This is a Currency method-call proof for Avalonia Android. It does not provide a general Avalonia host
package, native view hosting, incoming UI callbacks, event-stream lifecycle, or desktop support.
The separate [iOS sample](../Currency.iOS/README.md) has its own simulator verification;
Android execution does not establish iOS support.
Those integrations remain separate [roadmap](../../../../docs/roadmap.md) work. See the
[verification record](../../../../docs/verification.md) for executed evidence; the presence of this sample
alone does not establish runtime support.
