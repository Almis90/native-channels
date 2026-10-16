# Windows integration

Windows support includes module factories, messaging, and native views. The transport and MAUI packages
target `net10.0-windows10.0.19041.0`, with a declared minimum OS of Windows 10 build 17763.
See the [verification record](verification.md#windows) for tested configurations.

Windows adapters use C# to call WinRT, COM, P/Invoke, or other Windows SDKs. Each transport owns a
separate adapter-side `ChannelService`. The existing codecs and binary messenger handle both directions,
including reentrant callbacks, independent streams, cancellation and channel ownership. No Kotlin or
Swift binding package is needed on Windows. This is an in-process bridge, not an IPC protocol.

The [C++ and Rust experiment](https://github.com/Almis90/native-channels/tree/main/native/windows/interop)
demonstrates native DLL implementations behind a small shared C# channel adapter.
Run `./eng/verify-windows-interop.ps1` to build both DLLs and execute their channel
checks on Windows x64. This is separate from MAUI application and MSIX verification.

## Build and verify

On Windows, install the SDK pinned in `global.json` and its `maui-windows` workload:

```powershell
dotnet workload install maui-windows
./eng/verify-windows.ps1
```

The script also recognizes a local SDK at `.tools/dotnet/dotnet.exe`. It runs the portable tests,
packs the three Windows framework packages into `artifacts/windows/packages`, checks their Windows
assemblies and dependencies, and restores the framework sample into a new isolated NuGet cache.
It publishes an unpackaged x64 app, runs its verification, captures `sample-result.txt`, and closes
the process it started. An interactive Windows desktop is needed for runtime verification.

Use `./eng/verify-windows.ps1 -BuildOnly` for a compile-only check, as the Windows CI job does.
Packages default to `0.1.0-preview.1`; `-Version` overrides the development version. These packages contain
Windows assets only and are kept separate from the mobile release feed. Do not publish them as a
combined platform release.

Use `-Currency -PackOnly` to produce framework and Currency inputs for package assembly without
publishing or running the apps. Source builds use framework `0.1.0-preview.1` and Currency `1.1.0-alpha.1`
on both the Windows and mobile hosts.

To build just the library:

```powershell
dotnet build src/NativeChannels.Maui -c Release -p:NativeChannelsWindowsOnly=true
```

`NativeChannelsWindowsOnly` selects Windows without requiring mobile workloads. Normal Windows-host
project evaluation also includes the mobile frameworks. The sample follows Microsoft's
[unpackaged Windows deployment guidance](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli?view=net-maui-10.0).

## Signed MSIX deployment

Both samples include Windows manifests and icons. Their default deployment remains unpackaged;
`-Msix` builds a signed installer with minimum Windows version 10.0.17763.0. It includes the .NET
runtime and uses the Windows App Runtime framework package shipped beside the installer.
Self-contained Windows App SDK MSIX activation is unsupported on Windows 10 1809; see Microsoft's
[deployment limitation](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-1#version-11).
The verifier checks the generated package's signature entry, architecture, minimum OS and runtime
dependency, and records installer and dependency SHA-256 hashes alongside the NuGet hashes.
The samples also set `WindowsAppSdkDeploymentManagerInitialize=false` for MSIX because they use no
Main/Singleton features, such as push notifications. This follows Microsoft's
[auto-initializer guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties#the-deployment-manager-auto-initializer)
and avoids unnecessary runtime component installation at startup on older Windows builds. Apps that
use those features must initialize the Deployment Manager.

Create a development signing certificate on the build machine, with the publisher used by the samples:

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=NativeChannels Development' `
    -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddDays(7) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
Export-Certificate -Cert $cert -FilePath ./test-signing.cer
./eng/verify-windows.ps1 -PackageFeed artifacts/packages -Msix -BuildOnly `
    -CertificateThumbprint $cert.Thumbprint
./eng/verify-windows.ps1 -PackageFeed artifacts/packages -Msix -BuildOnly -Currency `
    -CertificateThumbprint $cert.Thumbprint
```

The printed `MSIX:` path identifies each installer. Copy it, its matching architecture's
`Dependencies/x64/` (or `Dependencies/arm64/`) files, the public `.cer`, and
`eng/verify-windows-msix.ps1` to a test machine. Trust the certificate in that machine's
Local Machine **Trusted People** store using an administrator session. Older Windows 10 builds also
need signed-app sideloading enabled. Follow Microsoft's
[MSIX signing and test deployment guidance](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
These are temporary development certificates, not Store or production signing.

Run the installation verifier from the signed-in user's ordinary desktop session:

```powershell
./verify-windows-msix.ps1 -PackagePath ./Currency.Sample_0.1.0.1_x64.msix `
    -DependencyPath ./Dependencies/x64/Microsoft.WindowsAppRuntime.1.8.msix `
    -OutputDirectory ./currency-msix-results
```

It refuses to replace an existing sample installation, installs the package, activates its app ID,
checks the running process's package identity, and reads the result from the package's `LocalState`.
It closes the app and uninstalls its package, including after a runtime failure. Retained evidence
includes the OS build, package and dependency hashes, activation identity, result and removal status.
The verifier does not explicitly uninstall shared runtime dependencies; Windows may remove unused
dependencies when the app is removed. During test-machine cleanup, remove only remaining test-added
dependencies, preserving any that were present before testing.
The verifier does not change certificate trust or sideloading settings; remove temporary trust and
restore any changed settings when testing is complete. If trust is already configured on the build
machine, omit `-BuildOnly` to build and run this installation check in one command.

## Register a module

Implement `IWindowsChannelModule` in Windows-specific source. A module factory must return a fresh
instance for each transport. `Register` installs handlers; `Dispose` releases SDK listeners and
resources. The transport closes its endpoints and disposes modules in reverse registration order,
including modules whose registration failed.

```csharp
#if WINDOWS
sealed class WindowsDeviceModule : IWindowsChannelModule
{
    private MethodChannel? _channel;

    public void Register(IChannelService channels)
    {
        _channel = channels.GetMethodChannel("device");
        _channel.SetMethodCallHandler((call, _) => call.Method switch
        {
            "name" => ValueTask.FromResult<object?>(Environment.MachineName),
            _ => throw new MethodNotImplementedException(call.Method),
        });
    }

    public void Dispose() => _channel?.Dispose();
}
#endif
```

Register the factory at startup alongside the other platforms' entry points:

```csharp
builder.UseNativeChannels(options => options.Modules.Add(
    new("example.DeviceModule", "DeviceModule")
    {
#if WINDOWS
        WindowsFactory = () => new WindowsDeviceModule(),
#endif
    }));
```

A registered module without a Windows factory fails during initialization with an actionable error.
Use `MauiChannelDispatchers.MainThread` for handlers that access WinUI. Initialization must not block
waiting for calls to the application's endpoint, which becomes available after construction.
Module disposal can run off the UI thread; marshal any SDK-specific UI cleanup explicitly.

## Embed a WinUI view

On Windows, `NativeViewFactoryRegistration.WindowsFactory` receives a `WindowsViewContext` containing
the adapter's channels, instance ID, channel name, and decoded creation parameters. Return a
`WindowsNativeView` containing a WinUI `UIElement` and an optional cleanup action.

```csharp
options.ViewFactories.Add(new("example.button", "example.ButtonFactory", "ButtonFactory")
{
#if WINDOWS
    WindowsFactory = context =>
    {
        var channel = context.Channels.GetMethodChannel(context.ChannelName,
            new() { HandlerDispatcher = MauiChannelDispatchers.MainThread });
        var button = new Microsoft.UI.Xaml.Controls.Button { Content = "Native Windows" };
        channel.SetMethodCallHandler((call, _) => call.Method == "title"
            ? ValueTask.FromResult(button.Content)
            : throw new MethodNotImplementedException(call.Method));
        return new WindowsNativeView(button, channel.Dispose);
    },
#endif
});
```

The factory and view cleanup run on the UI thread. Detach invalidates the channel, removes the element,
and invokes cleanup once. Reconnect creates a new endpoint, identity, and element. If a factory throws
before returning its host, it must release any SDK listeners it already created. The
[framework sample](sample.md) includes a bidirectional Windows button and reconnect stress checks.

The [Currency reference plugin](currency.md#windows-development-preview) now includes a Windows adapter.
Run `./eng/verify-windows.ps1 -Currency` to build its Windows-only preview package, execute native API
tests, and run the separate app. The PDF, audio and volume adapters also have Windows implementations:

```powershell
./eng/verify-windows.ps1 -Plugin PdfViewer
./eng/verify-windows.ps1 -Plugin AudioStreamer
./eng/verify-windows.ps1 -Plugin VolumeMonitor
```

These commands pack the selected plugin and run its native tests and package-only sample. Audio tests
send silent PCM through a real output device. The volume test temporarily changes the endpoint level
by two percentage points, verifies two independent listeners, and restores the original level.
`-BuildOnly` excludes audio hardware tests; it still runs the PDF renderer and registration/protocol tests.
The installed-app verifier recognizes all five samples. Add `-Msix -CertificateThumbprint THUMBPRINT`
when building signed installers, using the certificate setup described above.

`-PackageFeed` expects combined packages. Add `-WindowsOnlyFeed` for local Windows-only development
archives. `-PackageCache PATH` explicitly reuses a cache across consumers to save disk space; the
default creates a fresh cache. In both cases, restored first-party archives must match the feed hashes.
See [Windows plugin verification](windows-plugins-verification.md) for results and remaining coverage.

## Independent Windows CI

Use **Verify framework** with `platforms=Windows`. It builds Core once, prepares Windows framework
packages once, then compiles the framework sample for x64 and ARM64 in independent jobs. No Android
or Apple artifacts are needed. Hosted Windows verification establishes compilation, not desktop execution. See the
[Windows-only workflow commands](building.md#windows-only-framework-changes).

For separate consumer runs, select `package_source=Release` for the release gate feed,
or `package_source=Example` when using the separate optional example workflow. These feed types are not interchangeable.

## Combined platform packages

The manual **Release / Package gate** consumes a successful **All / Runtime** framework run at the
same commit. It reuses its Core, native and Windows artifacts and assembles the combined mobile payloads.
`eng/combine-packages.py` combines the transport, MAUI and
framework `.nupkg` and `.snupkg` files by using `--scope framework`; the optional example workflow
adds one selected plugin with `--scope example --plugin NAME`. It preserves the mobile native payloads, adds the Windows
assemblies and dependency groups, and rejects conflicting versions, commits, metadata, shared files,
missing frameworks or symbols. Core and the mobile binding packages retain the mobile build's bytes.

To assemble inputs from the same clean commit manually:

```sh
python3 eng/combine-packages.py --mobile artifacts/packages --windows artifacts/windows/packages \
  --output artifacts/combined-packages --source-commit FULL_GIT_COMMIT --scope framework
```

Both input feeds must include their symbol packages. The destination must be empty. The script audits
the output and records package hashes in `package-audit.json`. It does not publish or sign packages.

The `package-feed` artifact contains the combined framework output and sample adapters. Optional
plugin builds produce a separate `example-package-feed` artifact. Separate
[verification workflows](building.md#verify-the-framework-sample) accept its successful run ID and
require the exact same source commit. Windows consumers restore the same feed into fresh caches,
with archive hash checks and source mapping that requires the local first-party packages. Select one
sample and either x64 or ARM64 per run; applicable native API tests execute on the hosted x64 machine.

To verify an optional Currency example feed on an interactive Windows desktop:

```powershell
./eng/verify-windows.ps1 -PackageFeed artifacts/packages
./eng/verify-windows.ps1 -PackageFeed artifacts/packages -Currency
# Use these options on an ARM64 machine for native runtime verification:
./eng/verify-windows.ps1 -PackageFeed artifacts/packages -RuntimeIdentifier win-arm64
./eng/verify-windows.ps1 -PackageFeed artifacts/packages -RuntimeIdentifier win-arm64 -Currency
```

`-PackageFeed` consumes existing combined archives without repacking. Add `-BuildOnly` to cross-compile
for the requested architecture. Each successful check writes `verification.json` with OS, host
architecture, target RID, package hashes and whether the result was compilation or execution. An
interactive pass also retains the sample result text; the Currency tests retain their TRX report.

For framework releases, verify the framework feed. Verify any example being released separately
with its matching optional example feed:

| Host | Required evidence |
| --- | --- |
| Windows 11 x64 | Native API tests, application lifecycle, and signed installer checks |
| Windows 11 ARM64 | Native ARM64 tests and execution; cross-compilation is insufficient |
| Windows 10 22H2 x64 | Native API tests and separate package-only applications |
| Windows 10 LTSC 2019 x64, minimum build 17763 | Native API tests, applications, and installed MSIX behavior |
| Windows 10 ARM64 | Native ARM64 execution and OS/deployment validation |

The current preview's acceptance is pending. Use [platform verification](verification.md#windows)
and [Windows plugin verification](windows-plugins-verification.md) to record results. The verifier
defaults to unpackaged applications; `-Msix` selects separate installer verification. Store
publication, architecture coverage, and runtime execution must each be reported independently.
