param([string]$Dotnet = '', [string]$ReuseCache = '', [switch]$HostProbe)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Dotnet) { $Dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe' }
$project = Join-Path $repo 'samples/Uno/Feasibility/UnoFeasibility/UnoFeasibility.csproj'
$config = Join-Path $repo 'samples/Uno/Feasibility/NuGet.Config'
$output = Join-Path $repo ('artifacts/uno-feasibility/runs/' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $repo 'artifacts/uno-feasibility/packages'
$cache = if ($ReuseCache) { (Resolve-Path -LiteralPath $ReuseCache).Path } else { Join-Path $output 'cache' }
New-Item -ItemType Directory -Force $output, $feed | Out-Null
function Invoke-ProbeDotnet {
    & $Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

Push-Location $repo
try {
    # Rebuild the runtime-selected desktop transport from this checkout.
    Invoke-ProbeDotnet pack src/NativeChannels.Core/NativeChannels.Core.csproj -c Release '-p:Version=0.3.0-preview.4' -o $feed
    Invoke-ProbeDotnet pack src/NativeChannels.Uno/NativeChannels.Uno.csproj -c Release '-p:Version=0.3.0-preview.4' -o $feed
    Invoke-ProbeDotnet pack src/NativeChannels/NativeChannels.csproj -c Release '-p:Version=0.3.0-preview.4' '-p:NativeChannelsDesktopOnly=true' -o $feed
    $settings = @('-p:TargetFrameworks=net10.0-desktop', "-p:RestorePackagesPath=$cache")
    Invoke-ProbeDotnet restore $project @settings '-p:Configuration=Release' --configfile $config
    Invoke-ProbeDotnet build $project @settings -c Release --no-restore
    $binary = Join-Path (Split-Path $project) 'bin/Release/net10.0-desktop/UnoFeasibility.dll'
    $dependencyJson = & $Dotnet $binary --probe-dependencies
    if ($LASTEXITCODE -ne 0) { throw 'Dependency probe failed to execute.' }
    $dependencyJson | Set-Content -LiteralPath (Join-Path $output 'dependencies.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path (Split-Path $project) 'obj/project.assets.json') -Destination $output
    $hashes = Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | Get-FileHash -Algorithm SHA256
    $hashes | Select-Object Path, Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'packages.json') -Encoding utf8
    @{ freshCache = -not [bool]$ReuseCache; cache = $cache } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $output 'restore-mode.json') -Encoding utf8

    if ($env:OS -eq 'Windows_NT') {
        $previousRoot = $env:DOTNET_ROOT
        $previousResult = $env:NATIVECHANNELS_UNO_PROBE_RESULT
        $previousHostResult = $env:NATIVECHANNELS_UNO_HOST_PROBE_RESULT
        try {
            $env:DOTNET_ROOT = Split-Path $Dotnet
            $env:NATIVECHANNELS_UNO_PROBE_RESULT = Join-Path $output 'windows-view.json'
            $env:NATIVECHANNELS_UNO_HOST_PROBE_RESULT = if ($HostProbe) { Join-Path $output 'windows-host.json' } else { $null }
            $process = Start-Process -FilePath ([IO.Path]::ChangeExtension($binary, '.exe')) -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $output 'windows-stdout.log') `
                -RedirectStandardError (Join-Path $output 'windows-stderr.log')
            if (-not $process.WaitForExit(30000)) {
                Stop-Process -Id $process.Id
                throw 'Native view probe timed out after 30 seconds.'
            }
            $resultFile = if ($HostProbe) { $env:NATIVECHANNELS_UNO_HOST_PROBE_RESULT } else { $env:NATIVECHANNELS_UNO_PROBE_RESULT }
            if (-not (Test-Path -LiteralPath $resultFile)) {
                throw "UI probe produced no result. Inspect $output"
            }
            if ($HostProbe -and (Get-Content -Raw -LiteralPath $resultFile | ConvertFrom-Json).host -ne 'passed') {
                throw "Uno host probe failed. Inspect $resultFile"
            }
        }
        finally {
            $env:DOTNET_ROOT = $previousRoot
            $env:NATIVECHANNELS_UNO_PROBE_RESULT = $previousResult
            $env:NATIVECHANNELS_UNO_HOST_PROBE_RESULT = $previousHostResult
        }
    }
    Write-Output "Evidence: $output"
    Write-Output 'Probe execution completed. Inspect the JSON outcomes; this is NOT six-platform acceptance.'
}
finally { Pop-Location }
