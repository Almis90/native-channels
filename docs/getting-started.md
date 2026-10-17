# Getting started

Install NativeChannels in a .NET 10 MAUI application, register a native adapter, and make your first
call. The walkthrough uses the repository's Kotlin and Swift `echo` adapters. For a Windows adapter,
follow [Windows integration](windows-integration.md); to distribute your own integration, continue
with [plugin authoring](plugin-authoring.md).

::: info Preview release
[NativeChannels.Maui 0.1.0-preview.1](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.1)
is available on NuGet.org. It supports MAUI on Android, iOS, Mac Catalyst, and Windows.
See the [verification record](verification.md) for executed checks and remaining platform gaps.
:::

## Prerequisites

| Tool | Pinned version |
| --- | --- |
| .NET SDK / workload set | 10.0.401 |
| MAUI | 10.0.110 |
| C# | 14 |
| Xcode for Apple builds | 26.6 |
| JDK | 21 |
| Android SDK platform / build tools | 36 / 36.0.0 |

A Mac is needed to build Apple applications and native adapters. The portable core requires only the pinned .NET SDK.
Set `JAVA_HOME` and `ANDROID_HOME` if your tools use nondefault locations.

## 1. Install the package

From your MAUI application's directory:

```sh
dotnet add package NativeChannels.Maui --version 0.1.0-preview.1
```

Use NuGet.org as a package source. No local feed or reference to the NativeChannels source projects
is needed. The resulting project reference is:

```xml
<ItemGroup>
  <PackageReference Include="NativeChannels.Maui" Version="0.1.0-preview.1" />
</ItemGroup>
```

If your app uses central package management, put the version in its `Directory.Packages.props` instead.
The checked-in sample demonstrates that form.

`NativeChannels.Core`, `NativeChannels`, and the applicable native binding resolve transitively.
The Android binding contains the generic Kotlin bridge; the Apple binding covers iOS and Mac Catalyst.
Windows support lives in `NativeChannels` and `NativeChannels.Maui`, without a separate binding package.

Use the toolchain above for the verified configuration; the repository's `global.json` pins its SDK
and workload set. Use MAUI 10.0.110 in your app.
Target `net10.0-android`, `net10.0-ios`, and/or `net10.0-maccatalyst` for Apple and Android applications.
For Windows, follow [Windows integration](windows-integration.md).

Set the app's minimum platform versions to match the bridge. In a new MAUI template, **change the
existing Android `SupportedOSPlatformVersion` to `24`**; the template's lower default causes a
manifest merge error. The corresponding project settings are:

```xml
<PropertyGroup>
  <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">24</SupportedOSPlatformVersion>
  <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios' Or $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">15.0</SupportedOSPlatformVersion>
</PropertyGroup>
```

Windows requires a minimum of Windows 10 build 17763; see its integration guide for deployment details.

## 2. Include the native adapters

The package supplies the bridge. Your adapter supplies the named operations and any third-party SDK
dependencies. For this walkthrough, clone the repository beside your app and build its sample adapters.
Use [source revision `09ae4eb`](https://github.com/Almis90/native-channels/tree/09ae4eb862bc142c0281ded67413ea859e179c6d)
to match the published `0.1.0-preview.1` bridge.
Run these commands from the repository root on a Mac for both platforms, or just the Android build
for Android:

```sh
bash eng/bootstrap.sh --platforms
bash eng/build-android.sh
python3 eng/build-apple.py
```

This compiles the native bridge and adapter inputs; your app continues to consume the published
framework packages. You do not need to run `eng/pack.sh` for this walkthrough. For your own adapter,
use the [Android](android-integration.md) or [Apple](ios-integration.md) guide instead.

Copy these generated artifacts from the repository into your application's `Native` directory:

- `native/android/sample/build/outputs/aar/sample-release.aar`
- `artifacts/native/NativeChannelsSample.xcframework` (the whole directory)

Add platform-specific project items:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
  <AndroidLibrary Include="Native/sample-release.aar" Bind="false" />
</ItemGroup>
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios' Or $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">
  <NativeReference Include="Native/NativeChannelsSample.xcframework">
    <Kind>Framework</Kind>
    <SmartLink>false</SmartLink>
  </NativeReference>
</ItemGroup>
```

The generic bridge comes through NuGet; these assets supply the sample-specific operations. An adapter
AAR or XCFramework does not automatically supply every third-party native dependency your SDK needs.

## 3. Register the native module

In `MauiProgram.CreateMauiApp`, after creating the builder and before `builder.Build()`:

```csharp
using NativeChannels;

builder.UseNativeChannels(options =>
{
    options.Modules.Add(new NativeModuleRegistration(
        "dev.nativechannels.sample.SampleModule",
        "MCSampleModule",
        "NativeChannelsSample"));
});
```

Names are case sensitive. The Android name includes its package; the Apple class uses its explicit
Objective-C name, and the framework name omits `.framework`. Registration installs handlers at
startup. A missing module or framework produces a startup error.

Call `UseNativeChannels` once. If other plugins use it too, add their module or factory registrations
inside the same configuration callback. Plugin packages can supply the native assets transitively;
manual adapter imports above are for this direct integration with the sample module.

## 4. Make a call through dependency injection

Register a service alongside the module configuration:

```csharp
builder.Services.AddTransient<NativeEchoService>();
```

Then inject that service into a page or view model. Its implementation can be:

```csharp
using NativeChannels;

public sealed class NativeEchoService(IChannelService channels)
{
    public async Task<string?> EchoAsync(string message, CancellationToken cancellationToken = default)
    {
        using var channel = channels.GetMethodChannel("sample");
        return await channel.InvokeAsync<string>("echo", message, cancellationToken);
    }
}
```

`EchoAsync("Hello")` returns `"Hello"` after a native round trip. The per-call handle is suitable for
outgoing calls; keep a longer-lived handle when you need an incoming handler.

## 5. Restore and build your application

From your application's directory, build for an Android ARM64 device:

```sh
dotnet restore -p:TargetFrameworks=net10.0-android -r android-arm64
dotnet build -c Release -f net10.0-android -r android-arm64 --no-restore
```

Use the matching runtime identifier for your emulator/device, then deploy through your MAUI tooling.
The repository's verification scripts target its sample application ID, not your application's ID.
If you installed the SDK through the repository bootstrap, replace `dotnet` with
`bash ../native-channels/eng/dotnet.sh` for that sibling-directory layout.
Keep `bin` and `obj` out of your app's version control.

## Build from source

To test changes to NativeChannels itself, use [building from source](building.md) and the
[framework sample](../samples/NativeChannels.Sample/README.md). Those development checks intentionally
use local packages and isolated caches. When replacing a local package with the same version, clean
consumer outputs and use a fresh cache; see [troubleshooting](troubleshooting.md).

## Next steps

- Register a [native-to-managed handler](method-channels.md#receive-calls-from-native-code).
- Configure [timeouts and dispatch](configuration.md).
- Return a [source-generated DTO](message-codecs.md#typed-dtos).
- Write your own [Android](android-integration.md) or [Apple](ios-integration.md) module.
- Package the integration using [plugin authoring](plugin-authoring.md).
- Diagnose setup failures with [troubleshooting](troubleshooting.md).
