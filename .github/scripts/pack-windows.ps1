param(
    [ValidateSet('Framework', 'Plugin')]
    [string]$Scope = 'Framework',
    [ValidateSet('Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer')]
    [string]$Plugin
)

$ErrorActionPreference = 'Stop'
if (($Scope -eq 'Plugin') -ne [bool]$Plugin) { throw 'Specify Plugin only with Scope Plugin.' }
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repoRoot
try {
    $feed = Join-Path $repoRoot 'artifacts/windows/packages'
    New-Item -ItemType Directory -Force $feed | Out-Null
    if ($Scope -eq 'Framework') {
        $projects = @('NativeChannels.Core', 'NativeChannels', 'NativeChannels.Maui')
        $sharedSettings = @('-p:RestoreSources=https://api.nuget.org/v3/index.json')
        if ($env:NATIVECHANNELS_SHARED_CORE -eq 'true') {
            $projects = @('NativeChannels', 'NativeChannels.Maui')
            Copy-Item artifacts/packages/NativeChannels.Core.* $feed
            $sharedSettings = @('-p:NativeChannelsUseCorePackage=true',
                "-p:RestoreConfigFile=$repoRoot/artifacts/shared-core.NuGet.Config",
                "-p:RestorePackagesPath=$repoRoot/.tools/wn/framework")
        }
        foreach ($name in $projects) {
            Invoke-Dotnet pack "src/$name" -c Release '-p:NativeChannelsWindowsOnly=true' @sharedSettings -o $feed
        }
        if ($env:NATIVECHANNELS_SHARED_CORE -eq 'true') {
            & python .github/scripts/shared-core.py verify --feed $feed
            if ($LASTEXITCODE -ne 0) { throw 'Shared Core verification failed.' }
        }
        return
    }

    # The framework packages come from a verified same-commit build. Never rebuild them per plugin.
    $config = Join-Path $repoRoot 'artifacts/windows/NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    @"
<configuration><packageSources><clear />
<add key="local" value="$escapedFeed" />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources><packageSourceMapping>
<packageSource key="local"><package pattern="NativeChannels*" /><package pattern="Plugin.*" /></packageSource>
<packageSource key="nuget.org"><package pattern="*" /></packageSource>
</packageSourceMapping></configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8
    foreach ($name in @($Plugin)) {
        $packageId = if ($name -eq 'PdfViewer') { 'Plugin.Maui.PdfViewer' } else { "Plugin.$name" }
        $project = "plugins/$packageId/src/$packageId/$packageId.csproj"
        $cache = Join-Path $repoRoot ('.tools/wn/' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
        $settings = @('-p:NativeChannelsWindowsOnly=true', "-p:RestorePackagesPath=$cache")
        Invoke-Dotnet restore $project @settings --configfile $config
        Invoke-Dotnet pack $project @settings -c Release --no-restore -o $feed
    }
}
finally { Pop-Location }
