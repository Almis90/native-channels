# ChannelExplorer

A MAUI application that consumes NativeChannels through **NuGet package references only** and includes
its native adapters explicitly. It is the framework's executable interoperability check.
For the practical native-view example, use [PDF Viewer](../PdfViewer/README.md).

## Prepare

Run the [native build and packaging steps](../../../docs/building.md) from the repository root first.
The commands below also run from that root. Restore scripts clean this sample's generated `bin`/`obj`
and use an isolated package cache, so restore/build one target at a time.

## Android

Start an emulator and use its serial from `adb devices`:

```sh
bash eng/restore-sample.sh -p:TargetFrameworks=net10.0-android -r android-arm64
bash eng/dotnet.sh build samples/Maui/ChannelExplorer -c Release -f net10.0-android -r android-arm64 --no-restore
bash eng/verify-android-sample.sh EMULATOR_SERIAL Release android-arm64
```

Use `android-x64` in all three commands for an x86_64 emulator.

## iOS simulator

Choose and boot an available simulator with Xcode or `xcrun simctl`:

```sh
bash eng/restore-sample.sh -p:TargetFrameworks=net10.0-ios -r iossimulator-arm64
bash eng/dotnet.sh build samples/Maui/ChannelExplorer -c Release -f net10.0-ios -r iossimulator-arm64 --no-restore
bash eng/verify-ios-sample.sh SIMULATOR_UDID Release
```

## Mac Catalyst

```sh
bash eng/restore-sample.sh -p:TargetFrameworks=net10.0-maccatalyst -r maccatalyst-arm64
bash eng/dotnet.sh build samples/Maui/ChannelExplorer -f net10.0-maccatalyst -r maccatalyst-arm64 --no-restore
bash eng/verify-catalyst-sample.sh Debug maccatalyst-arm64
```

## Expected result

The app automatically checks typed calls, DTOs, bytes, null, structured errors, native-to-managed
callbacks, UI dispatch, concurrent calls, cancellation, timeouts, handle disposal, and session isolation.
It also exercises bidirectional JSON/string/binary/standard messages, null events, completion, structured stream
errors, two simultaneous native subscriptions, and native cleanup when each subscription ends.
Standard messages check typed numeric arrays, IEEE values, nested collections, arbitrary map keys,
and Int32/Int64 preservation across native callbacks.
Its two native buttons are test fixtures: it checks independent state and creation parameters, invokes C#
from native clicks, and verifies reconnect, stale-channel rejection, pending-call cancellation, and UI dispatch.
You can tap both buttons or recreate the first after verification. See [native views](../../../docs/native-views.md)
and [ViewVerification.cs](ViewVerification.cs).
It displays its result and writes `sample-result.txt`. Verification scripts report PASS or fail with
a nonzero exit code. Android additionally logs `NATIVECHANNELS_SAMPLE PASS android: …`.

Read [NativeVerification.cs](NativeVerification.cs) for the named checks, [MainPage.cs](MainPage.cs)
for result presentation, and [MauiProgram.cs](MauiProgram.cs) for registration.
Native implementations are in [SampleModule.kt](../../../native/android/sample/src/main/java/dev/nativechannels/sample/SampleModule.kt)
[SampleModule.swift](../../../native/apple/Samples/SampleModule.swift),
and [WindowsSampleModule.cs](../../../native/windows/sample/WindowsSampleModule.cs).

For your own app, follow [getting started](../../../docs/getting-started.md). For a failure, use
[troubleshooting](../../../docs/troubleshooting.md) and distinguish build success from runtime success.

## Windows

From a Windows checkout with the pinned SDK and `maui-windows` workload, run
`./eng/verify-windows.ps1` from the repository root. It builds isolated Windows packages, publishes
this package-only sample, and verifies methods, messages, streams and native WinUI buttons.
Use `-BuildOnly` to skip execution. See [Windows integration](../../../docs/windows-integration.md).
