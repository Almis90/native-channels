# Android native integration

Write SDK-specific operations in a Kotlin/Java adapter AAR. The .NET binding exposes generic bridge
operations; your adapter's individual methods stay native.

## Create a module

This complete Kotlin module exposes `echo` and a callback into a managed `getToken` handler on the
`example` channel:

```kotlin
package dev.example

import dev.nativechannels.MethodChannel
import dev.nativechannels.NativeBridge
import dev.nativechannels.NativeModule

class ExampleModule : NativeModule {
    override fun register(bridge: NativeBridge) {
        val channel = MethodChannel("example", bridge)
        channel.setHandler { call, result ->
            when (call.method) {
                "echo" -> result.success(call.arguments)
                "callback" -> {
                    val requestId = channel.invoke("getToken") { value, error ->
                        if (error != null) result.error(error.code, error.message, error.details)
                        else result.success(value)
                    }
                    result.onCancel { channel.cancel(requestId) }
                }
                else -> result.notImplemented()
            }
        }
    }
}
```

The module needs a public parameterless constructor. Registration should install handlers, not start
managed requests: the managed receiver attaches after module registration. For the callback above,
register a C# handler on `example` before invoking `callback`; see [MethodChannel](method-channels.md).

Use `call.argument("name")` to read a map entry. Validate its type and throw a native `ChannelError`
with `invalid_arguments` for invalid application input. Supported types are listed in [codecs](message-codecs.md).

## Build and include the adapter

The bundled Gradle project has `:library` for the generic bridge and `:sample` for an adapter:

```kotlin
dependencies {
    implementation(project(":library"))
}
```

Use the [sample build file](../native/android/sample/build.gradle.kts) as a starting point, then add
your SDK dependencies. Build with `bash eng/build-android.sh`. Its sample artifact is
`native/android/sample/build/outputs/aar/sample-release.aar`.

In the MAUI app, reference `NativeChannels.Maui` and include the adapter with binding disabled:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
  <AndroidLibrary Include="Native/example-release.aar" Bind="false" />
</ItemGroup>
```

Configure `dev.example.ExampleModule` as the Android class name in `NativeModuleRegistration`.
The Apple class/framework names configure the separate Apple adapter. See [getting started](getting-started.md)
for the full cross-platform registration.

## Dependencies and shrinking

The generic bridge depends on `Xamarin.Kotlin.StdLib` through NuGet. Despite its historical name, it
is a modern .NET Android package. Do not separately embed another Kotlin stdlib or JetBrains annotations
JAR: duplicate definitions fail D8/R8. An AAR does not contain all of its Maven dependencies; package
SDK dependencies explicitly in the consuming application.

Consumer rules preserve native module names and constructors. Add SDK-specific rules when required
by that SDK, and verify a Release consumer with R8. Do not infer shrinking correctness from Debug alone.

## Threads and cancellation

`setHandler(onMainThread = true)` enters the handler on Android's main thread. Complete `MethodResult`
from any thread using `success`, `error`, or `notImplemented`; duplicate replies are ignored.
Handlers should return promptly when starting asynchronous SDK work.

Use `result.onCancel { ... }` to connect cancellation to the SDK, and inspect `result.isCancelled`
when completing delayed work. The [sample module](../native/android/sample/src/main/java/dev/nativechannels/sample/SampleModule.kt)
shows interrupting a worker thread. Cancellation callbacks should be short and must not throw.

`channel.invoke(...)` returns a request ID. Retain it for a native-to-managed operation and call
`channel.cancel(id)` if its native owner ends or a native deadline expires. The managed service's
30-second outgoing-call default does not set a native caller's deadline.

## Verification and diagnostics

Run JVM tests, rebuild the AARs, repack, and restore the sample into a fresh cache after bridge changes.
`eng/verify-android-sample.sh` verifies actual APK execution. Debug embeds assemblies for `adb install`;
the sample's Release configuration additionally exercises trimming/AOT and R8.

A clean binding build may emit BG8605/BG8606 for Kotlin synthetic `$default` helpers.
`Transforms/Metadata.xml` excludes the native method/message/event and codec helper classes from the managed
binding surface. Inspect `java-resolution-report.log` if warnings name other types or members.
See [building](building.md) and [troubleshooting](troubleshooting.md).

For value/reply contracts, use [BasicMessageChannel](basic-message-channels.md#kotlin). For independent
native subscriptions, use [EventChannel](event-channels.md#kotlin-producer) and register SDK cleanup
with each sink's `onTermination` callback. The same generic bridge and module packaging apply.
