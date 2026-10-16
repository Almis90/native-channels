---
description: Design a typed C# API, implement Kotlin and Swift adapters, and package a reusable NativeChannels plugin.
---

# Build a reusable plugin

A NativeChannels plugin packages a typed C# API together with Kotlin and Swift adapters. Applications
consume that API and configure the plugin at startup. SDK-specific operations stay in native code;
the existing generic bridge carries their requests and replies.

This guide follows [Plugin.Maui.Currency](currency.md), the smallest complete reference plugin.
Its [sample](currency-sample.md) consumes the local package without project references or manual
native asset imports. For streams, binary transfer, and views, see the other [examples](examples.md).

The setup and package dependencies below describe the current MAUI integration. Shared plugin
registration, complete Avalonia hosting (0.2), and Uno hosting (0.3) are [planned](roadmap.md).
The Android Currency proof is the first part of that work, not a completed Avalonia integration.
Future hosts must verify reusable plugins on each target; browser plugins require JavaScript
adapters and explicit capability limits, as described in the [browser plan](roadmap.md#browser-and-webassembly).

## 1. Define the contract

Design the C# API and native contract together. Give the endpoint a name belonging to your plugin
and specify values, error codes, threading, cancellation, and resource ownership before adding SDK calls.

Currency uses this contract:

| Part | Definition |
| --- | --- |
| C# API | `ICurrencyService.GetSymbolAsync(code, cancellationToken)` |
| Channel | `plugin.maui.currency` |
| Method | `getSymbol` |
| Argument | Three-letter currency code, normalized to uppercase |
| Result | Native symbol for the current display locale |
| Native error | `invalid_currency_code` for a well-formed code unknown to the platform |
| Ownership | DI owns the singleton service; the service owns its channel handle |

Use [MethodChannel](method-channels.md) for named SDK operations with typed results and structured
errors. A plugin can add basic messages for binary data, event channels for notifications, or view
factories for native UI. See [choose a channel](choosing-a-channel.md).

## 2. Implement the managed facade

Keep channel names and method strings inside the plugin. Application code should depend on a small
interface with familiar .NET types and cancellation tokens.

Currency's implementation validates input, performs the native call, checks the result, and disposes
its channel when the DI container ends its lifetime:

<<< ../plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/CurrencyService.cs

The facade owns a handle; disposing it leaves other handles usable. A long-lived service is suitable
for recurring calls. Subscription services should create an enumeration per caller and propagate its
cancellation. Native view wrappers must use the channel for the current connection and reinstall
handlers after reconnecting.

For DTO arguments or results, register source-generated `System.Text.Json` metadata. Define matching
property names and portable values in both native adapters. See [codecs and typed values](message-codecs.md).

### Keep adapters together

Organize platform adapters under one source directory:

```text
MyPlugin/
├── src/MyPlugin/           shared C# API and package project
└── native/
    ├── android/            Kotlin/Java adapter
    ├── apple/              Swift/Objective-C adapter
    └── windows/            C# adapter (or a separate native project)
```

The reference plugins include `../../native/windows/**/*.cs` from their `.csproj` only for the
Windows target. Those C# files compile into the plugin's Windows assembly; they do not need a
separate native binary. A C++ or other native implementation can live in the same directory with
its own build project and C# interop layer. See the
[Currency Windows adapter](../plugins/Plugin.Maui.Currency/native/windows/WindowsCurrencyModule.cs).

## 3. Implement native adapters

| Platform | Module | Registration identity |
| --- | --- | --- |
| Android | Kotlin class implementing `NativeModule` | Fully qualified class name, with a public parameterless constructor |
| Apple | Swift `NSObject` class implementing `NativeModule` | Explicit Objective-C class name plus framework name |

Use the same endpoint, method names, and value types in both implementations. Validate native inputs
as well as facade inputs: other callers can send directly to the endpoint. Return structured errors
with a documented code and useful details.

The working Currency adapters are
[CurrencyModule.kt](../plugins/Plugin.Maui.Currency/native/android/src/main/java/dev/nativechannels/currency/CurrencyModule.kt)
and [CurrencyModule.swift](../plugins/Plugin.Maui.Currency/native/apple/Sources/CurrencyModule.swift).
The [Android](android-integration.md) and [Apple](ios-integration.md) guides explain module construction,
async callbacks, UI dispatch, and cancellation.

Connect cancellation to native SDK work where supported. Observers, timers, and streams need cleanup
on subscription termination or bridge disposal. UI operations must enter the platform main thread.
Avoid blocking a native queue while waiting for C# to reply.

## 4. Provide startup extensions

Separate native module registration from DI service registration. Consumers add all plugin modules
inside the application's single `UseNativeChannels` call:

```csharp
using NativeChannels;
using Plugin.Maui.Currency;

builder.UseNativeChannels(options => options.AddCurrency());
builder.Services.AddCurrency();
```

The reference implementation centralizes module identities and avoids duplicate registration:

<<< ../plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/CurrencyExtensions.cs

Use the same pattern for your plugin's extension names and module identities. Native view plugins
add `NativeViewFactoryRegistration` entries to `options.ViewFactories`; see [native views](native-views.md).

## 5. Include native assets in the package

A plugin package contains its managed assemblies, adapters, and build imports:

```text
lib/net10.0-android/           managed facade
lib/net10.0-ios/              managed facade
lib/net10.0-maccatalyst/       managed facade
native/android/              adapter AAR
native/apple/                adapter XCFramework
buildTransitive/              native asset imports
```

The plugin depends on `NativeChannels.Maui`. Its package project packs the adapter artifacts and
`buildTransitive` targets. Those targets import assets relative to the installed package directory,
so the application needs no path to the plugin's source checkout.

Currency's actual imports are:

<<< ../plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/buildTransitive/Plugin.Maui.Currency.targets{xml}

Use [its package project](../plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/Plugin.Maui.Currency.csproj)
as the packaging reference. Give your plugin its own package ID, namespaces, channel names, native
class names, framework name, and target filename. `Plugin.Maui.<Feature>` is a naming pattern used
by these examples; NativeChannels does not require that prefix.

Package every required native SDK dependency too. An AAR does not bundle all Maven dependencies,
and an XCFramework does not automatically supply every framework it links against. Retain Android
consumer preservation rules and explicitly exported Objective-C registration classes.

## 6. Verify a separate package consumer

An application using project references can hide incomplete packages. Keep a second application that
references only the plugin package and restores from a local feed into a fresh cache.

For the Currency example, after building the framework packages:

```sh
native/android/gradlew -p native/android --no-daemon :currency:assembleRelease :currency:testReleaseUnitTest
python3 plugins/Plugin.Maui.Currency/eng/build-apple.py
bash plugins/Plugin.Maui.Currency/eng/pack.sh
python3 plugins/Plugin.Maui.Currency/eng/inspect-package.py

bash eng/restore-currency-sample.sh -p:TargetFrameworks=net10.0-ios -r iossimulator-arm64
bash eng/dotnet.sh build samples/Currency.Sample -c Release -f net10.0-ios -r iossimulator-arm64 --no-restore
bash eng/verify-ios-currency.sh SIMULATOR_UDID Release
```

The commands run from the repository root with the [pinned toolchain](building.md). Boot one
simulator first and replace `SIMULATOR_UDID` with its actual identifier. The
[Currency sample](currency-sample.md) includes Android and other target commands.

Check native loading, ordinary calls, errors, cancellation, callbacks, and cleanup through the package.
Release builds expose trimming, linking, and shrinking failures that Debug may hide. Compile iOS
device and Mac Catalyst targets separately and record execution separately from compilation.

Run the [package audit](release-checklist.md#distribution-audit) on the final archives and retain their
source commits and hashes. Packaging and verification do not publish anything to NuGet.org.

## Document the consumer contract

Your plugin's README should cover startup registration, the public C# API, supported platforms,
permissions, error codes, cancellation, threading, and ownership. Explain SDK limitations and record
what actually ran. Keep detailed native build instructions separate from the app-facing quick start.

The reference plugins live in this repository to teach integration and packaging. For your own
plugin, release and version it according to its public API and the framework versions you support.
