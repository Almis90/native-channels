# Troubleshooting and diagnostics

Start with the [build instructions](building.md) and compare your target with the
[verified platform matrix](verification.md). Report the package version, target framework, runtime
identifier, configuration, tool versions, and relevant error when opening an issue.

## Checking a native round trip

The package-only sample reports `NATIVECHANNELS_SAMPLE PASS` or a failure and writes
`sample-result.txt` in its application data directory. The scripts below install or launch the built
sample and check its result:

```sh
bash eng/verify-android-sample.sh EMULATOR_SERIAL Release android-arm64
bash eng/verify-ios-sample.sh SIMULATOR_UDID Release
bash eng/verify-catalyst-sample.sh Debug maccatalyst-arm64
```

Build and restore the matching target first. A successful build alone does not verify native loading
or callbacks. Android Debug builds embed managed assemblies so plain `adb install` can run the sample.
Keep Release checks too: trimming, linking, and shrinking can expose problems absent from Debug.

## Stale iOS AOT or linker state

Replacing a local package without changing its version can leave stale consumer outputs. Symptoms
include an AOT dependency-ID mismatch before startup. Run `eng/restore-sample.sh` for the target again;
it removes that consumer's `bin`/`obj`, creates an isolated package cache, and verifies package hashes.
Then rebuild. For a separate application, clean its generated outputs and restore from a fresh cache.

## Android duplicate classes or missing modules

The bridge depends on `Xamarin.Kotlin.StdLib` through NuGet. Do not also embed Kotlin stdlib or
JetBrains annotations JARs: duplicate definitions fail D8/R8. Package your adapter and its native
SDK dependencies explicitly. Module classes must implement `NativeModule`, have a public
parameterless constructor, and match the fully qualified name configured in C#.

The binding generator may report BG8605/BG8606 for Kotlin synthetic helpers excluded from the binding
surface. See [Android integration](android-integration.md); inspect `java-resolution-report.log` when
a warning names other types or members.

## Apple framework or class loading failures

Include the adapter XCFramework as a `NativeReference` with `Kind=Framework` and `SmartLink=false`.
Check the selected device/simulator/Catalyst slice, the framework name passed to
`NativeModuleRegistration`, and the module's explicit Objective-C name. The framework must load
before Objective-C class lookup. See [Apple integration](ios-integration.md).

## Calls fail, time out, or cannot deserialize

A missing channel, an unimplemented method, a native error, and a successful null result are distinct
outcomes; see [error handling](error-handling.md). Verify channel and method names and install handlers
during module registration. Configure source-generated System.Text.Json metadata for DTOs.

The default timeout is 30 seconds. Interactive operations can use an extended timeout or
`Timeout.InfiniteTimeSpan` at service, channel, or invocation level. Cancellation ends the caller's
wait and requests cooperative native cancellation; it cannot guarantee that an SDK stops its work.
See [MethodChannel](method-channels.md) and [codecs](message-codecs.md).
