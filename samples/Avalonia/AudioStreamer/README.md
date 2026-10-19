# AudioStreamer with Avalonia on Windows

This package-only desktop sample uses `NativeChannels.Avalonia` and the same
`Plugin.AudioStreamer` facade and Windows `waveOut` adapter as the MAUI sample.
It has no MAUI or Windows App SDK dependency. The package name stays unchanged for
existing consumers; MAUI applications now reference `NativeChannels.Maui` explicitly.

Play the original 16-second instrumental, loop it, adjust signal level, pause/resume,
or stop. The UI displays native buffer occupancy and enqueue wait. Startup checks
use silent PCM. Minimizing stops and flushes playback; restoring does not restart it.
Closing awaits playback, the window's DI session, and the host before exiting.

This first AudioStreamer Avalonia head targets **Windows 10 build 17763+**, with an
available default audio output. A [Linux head](Linux/README.md) shares this UI and uses ALSA.
Android/iOS Avalonia heads, native macOS and browser audio integration remain work for 0.2. Existing Android/iOS/Catalyst adapters
are retained, but their MAUI consumers need fresh acceptance after the dependency change.

## Build from local packages

From the repository root in PowerShell, use the pinned .NET SDK in `global.json`,
Python 3 and NuGet access. No MAUI workload is needed. Commands build development
archives into `artifacts/packages`; they do not publish packages or run workflows.

```powershell
dotnet pack src/NativeChannels.Core -c Release -o artifacts/packages
if ($LASTEXITCODE) { throw 'Core packaging failed' }
python eng/cache-dependencies.py
$cache = Join-Path (Get-Location) ('.tools/wn/audio-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $cache | Out-Null
python eng/consumer-config.py "$cache/NuGet.Config"
$projects = @(
    'src/NativeChannels',
    'src/NativeChannels.Avalonia',
    'plugins/Plugin.AudioStreamer/src/Plugin.AudioStreamer'
)
foreach ($project in $projects) {
    dotnet restore $project -p:NativeChannelsWindowsOnly=true -p:NativeChannelsUseCorePackage=true --packages $cache --configfile "$cache/NuGet.Config" --force --no-http-cache
    if ($LASTEXITCODE) { throw "Restore failed: $project" }
    python eng/verify-consumer-assets.py "$project/obj/project.assets.json"
    if ($LASTEXITCODE) { throw "Package provenance failed: $project" }
    dotnet pack $project -c Release -p:NativeChannelsWindowsOnly=true -p:NativeChannelsUseCorePackage=true --no-restore -o artifacts/packages
    if ($LASTEXITCODE) { throw "Pack failed: $project" }
}
python plugins/Plugin.AudioStreamer/eng/inspect-package.py --windows-only
dotnet restore samples/Avalonia/AudioStreamer -r win-x64 -p:SelfContained=true --packages $cache --configfile "$cache/NuGet.Config" --force --no-http-cache
python eng/verify-consumer-assets.py samples/Avalonia/AudioStreamer/obj/project.assets.json
dotnet publish samples/Avalonia/AudioStreamer -c Release -r win-x64 -p:SelfContained=true --no-restore -o artifacts/audio-avalonia/windows/app
```

Run `artifacts/audio-avalonia/windows/app/AudioStreamer.Windows.exe` for interactive
playback. Use the local verifier for silent automation:

```powershell
./eng/verify-audio-avalonia-windows.ps1 -AppDirectory artifacts/audio-avalonia/windows/app
```

`--verify` checks playback progress, bounded buffering, pause/resume, cancellation,
exact drain counts and restart. It also disposes three DI sessions from a worker
thread while writes are pending, recreates sessions, exercises playback controls and
minimize/restart, then closes the actual window with paused playback still active.
The runner requires exit code zero, a PASS result written after shutdown, and exactly
one `AUDIO_AVALONIA_CLOSED` marker. It saves logs and hashes of the executed files in
`artifacts/audio-avalonia/windows/run-*/`. These checks require an audio endpoint but
do not establish audible quality, routing behavior, or physical latency.

## Registration and ownership

```csharp
AppBuilder.Configure<App>().UsePlatformDetect().UseNativeChannels(services =>
{
    services.AddNativeChannels(options => options.AddAudioStreamer());
    services.AddAudioStreamer();
});
```

Create one host session per window with `app.GetNativeChannels().CreateSession()`;
resolve `IAudioStreamer` from its `Services`. Dispose each playback session before
opening another. The DI session owns the streamer and channel bridge as a final
cleanup boundary. Keep Avalonia's dispatcher alive until disposal finishes.

See the [plugin contract](../../../plugins/Plugin.AudioStreamer/README.md),
[reusable host](../../../docs/avalonia-host.md), and
[verification record](../../../plugins/Plugin.AudioStreamer/VERIFICATION.md).
