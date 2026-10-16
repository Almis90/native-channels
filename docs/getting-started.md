# Getting started

Make a native `echo` call from a separate MAUI application using the bundled Kotlin and Swift adapters.
Try the framework sample in step 1, then follow the remaining steps to configure your own application.
To distribute an integration to other apps, continue with [plugin authoring](plugin-authoring.md).

::: info Local packages
NativeChannels has not been published to NuGet.org. This guide builds the packages from source and
uses a local feed. Source builds use `0.1.0-preview.1`; see the [verification record](verification.md)
for executed checks and remaining platform gaps.
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

A Mac is needed to build the Apple frameworks. The portable core requires only the pinned .NET SDK.
Set `JAVA_HOME` and `ANDROID_HOME` if your tools use nondefault locations.

## 1. Build and try the sample

From the repository root:

```sh
bash eng/bootstrap.sh --platforms
bash eng/build-android.sh
python3 eng/build-apple.py
bash eng/pack.sh
python3 eng/inspect-packages.py
```

The packages are in `artifacts/packages`. They have not been published to NuGet.org.
Follow the [sample instructions](../samples/NativeChannels.Sample/README.md) to restore, build, and run
on an emulator or simulator before adding your own adapter.

## 2. Reference NativeChannels in your application

Keep your application outside the library's project-reference graph. For example:

```text
workspace/
  native-channels/
    artifacts/packages/
  MyApp/
    MyApp.csproj
    NuGet.Config
    Native/
```

For that layout, create `MyApp/NuGet.Config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nativechannels-local" value="../native-channels/artifacts/packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="nativechannels-local"><package pattern="NativeChannels*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

The local feed supplies NativeChannels; NuGet.org supplies external dependencies. Copy the repository's
`global.json` into `MyApp` to keep SDK/workload selection consistent, and use MAUI 10.0.110 in the app.
Target `net10.0-android`, `net10.0-ios`, and/or `net10.0-maccatalyst` for Apple and Android applications.
For Windows, follow [Windows integration](windows-integration.md).

Add this item to your app project:

```xml
<ItemGroup>
  <PackageReference Include="NativeChannels.Maui" Version="0.1.0-preview.1" />
</ItemGroup>
```

If your app uses central package management, put the version in its `Directory.Packages.props` instead.
The checked-in sample demonstrates that form.

## 3. Include the native adapters

Copy these generated artifacts into `MyApp/Native`:

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

## 4. Register the native module

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

## 5. Make a call through dependency injection

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

## 6. Restore and build your application

For the sibling-directory layout above, run these commands from the NativeChannels repository root.
They use its locally installed SDK and a package cache inside your application:

```sh
bash eng/dotnet.sh restore ../MyApp/MyApp.csproj --configfile ../MyApp/NuGet.Config --packages ../MyApp/.nuget/packages -p:TargetFrameworks=net10.0-android -r android-arm64
bash eng/dotnet.sh build ../MyApp/MyApp.csproj -c Release -f net10.0-android -r android-arm64 --no-restore
```

Use the matching runtime identifier for your emulator/device, then deploy through your MAUI tooling.
The repository's verification scripts target its sample application ID, not your application's ID.
Keep `.nuget`, `bin`, and `obj` out of your app's version control. When replacing a local package with
the same version, clean consumer outputs and use a fresh cache; see [troubleshooting](troubleshooting.md).

## Next steps

- Register a [native-to-managed handler](method-channels.md#receive-calls-from-native-code).
- Configure [timeouts and dispatch](configuration.md).
- Return a [source-generated DTO](message-codecs.md#typed-dtos).
- Write your own [Android](android-integration.md) or [Apple](ios-integration.md) module.
- Package the integration using [plugin authoring](plugin-authoring.md).
- Diagnose setup failures with [troubleshooting](troubleshooting.md).
