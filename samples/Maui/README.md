# MAUI samples

Each sample is a normal .NET MAUI single project, with Android, iOS, Mac Catalyst,
and Windows targets in one `.csproj`. Run build and verification commands from
the repository root.

| Sample | Project | Application ID |
| --- | --- | --- |
| [ChannelExplorer](ChannelExplorer/README.md) | `ChannelExplorer/ChannelExplorer.csproj` | `dev.nativechannels.sample` |
| [Currency](Currency/README.md) | `Currency/Currency.Sample.csproj` | `dev.nativechannels.currency.sample` |
| [AudioStreamer](AudioStreamer/README.md) | `AudioStreamer/AudioStreamer.Sample.csproj` | `dev.nativechannels.audio.sample` |
| [VolumeMonitor](VolumeMonitor/README.md) | `VolumeMonitor/VolumeMonitor.Sample.csproj` | `dev.nativechannels.volume.sample` |
| [PdfViewer](PdfViewer/README.md) | `PdfViewer/PdfViewer.Sample.csproj` | `dev.nativechannels.pdf.sample` |

## Template and layout

The layout was established with the official `.NET MAUI App` template supplied
by the pinned .NET SDK/workload (`10.0.401`), using `dotnet new maui --no-restore`
with each project's existing name (or `ChannelExplorer` for the renamed bridge
sample). To inspect a fresh template without changing a sample:

```sh
bash eng/dotnet.sh new maui --name MauiTemplate --output artifacts/maui-template --no-restore
```

Each application contains `Platforms/Android`, `Platforms/iOS`,
`Platforms/MacCatalyst`, `Platforms/Windows`, `Properties/launchSettings.json`,
and the template's `Resources/` layout for icons, splash screens, images, fonts,
styles, and raw assets. Existing application artwork is now in
`Resources/AppIcon/`; PDF Viewer keeps its document in `Resources/Raw/`.

The existing C# `App`, `MainPage`, navigation, platform startup code, minimum OS
versions, and verification scenarios replace the template's demonstration UI.
The template's style dictionaries and fonts are available but are not applied
to the existing UI. Application IDs and Windows package identities are unchanged.
ChannelExplorer uses that name for its project, assembly, namespaces, and title;
the NativeChannels libraries and native adapter identifiers retain their names.

## Package consumption and verification

Samples consume NativeChannels and plugins through NuGet packages. ChannelExplorer
also includes the sample's native adapters explicitly. Use the existing
`eng/restore-*-sample.sh` scripts (`eng/restore-sample.sh` for ChannelExplorer),
which validate packages against the local feed and clean only the selected
consumer. Restore and build one target at a time, because these scripts clear
that sample's `bin` and `obj` directories.

The framework-independent Currency checker lives in
[`samples/Currency.Shared/PluginVerification.cs`](../Currency.Shared/PluginVerification.cs)
and is linked into the Currency project. It can also be linked into an Avalonia
host without a dependency on MAUI, including its optional host-lifetime
cancellation token. Avalonia applications are not present on this
branch. `tests/PackageConsumer` remains a packaging test fixture rather than a
sample application.

See each sample's README and [building](../../docs/building.md) for commands.
A successful build does not execute the app: the Android, iOS, Catalyst, and
Windows verification scripts separately launch applications and check their
runtime results.
